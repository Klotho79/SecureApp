using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Transport;

/// <inheritdoc cref="IMessageTransport"/>
/// <remarks>
/// Implemented with <see cref="ClientWebSocket"/> (BCL, no extra package, works on all 4 targets).
/// Deliberately free of any MAUI-specific type — only BCL + Domain/Data interfaces — so it can be
/// exercised directly from a plain console test, the same pattern <c>DocumentBrowserViewModel</c>
/// already established for MAUI-decoupled logic.
///
/// Registered as a Singleton (one persistent connection for the app's lifetime), so its
/// <c>Scoped</c> dependencies (<see cref="IChatSessionRepository"/>, <see cref="ITransportSettingsRepository"/>)
/// are resolved through a short-lived <see cref="IServiceScopeFactory"/> scope on each use rather
/// than injected directly — avoids the classic captive-dependency problem.
///
/// No auto-reconnect/backoff loop in this pass — a single explicit connect/disconnect lifecycle.
/// Flagged as a known simplification for a later pass, not an oversight.
/// </remarks>
public sealed class WebSocketMessageTransport : IMessageTransport, IAsyncDisposable
{
    // ASP.NET Core's minimal API JSON binding uses JsonSerializerDefaults.Web (camelCase,
    // case-insensitive) for both request and response bodies — PostAsJsonAsync/ReadFromJsonAsync
    // default to plain JsonSerializerOptions.Default instead (case-sensitive), which would silently
    // fail to populate DeviceCredential's PascalCase properties from the relay's camelCase response.
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient = new();

    // ClientWebSocket.SendAsync must not run concurrently on one socket — a delivery ack, a user
    // message and a resend can all fire at once; without this the 2nd throws InvalidOperationException
    // (and the message is lost).
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Connect + authenticate gets this long (2026-10-24). On an unreachable relay (tunnel down, packets
    // silently dropped) the OS connect timeout is minutes, and the supervisor tick waits for it — so a relay
    // that came back a second later was not noticed until that attempt finally gave up.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    // One send (including waiting for the send lock) gets this long. A send on a half-dead connection can hang
    // for minutes, and every ack / message / invite queues behind it on the lock; cancelling a send aborts the
    // socket, so the receive loop ends and the supervisor reconnects at once. Frames are small (attachments go
    // through the library, not the socket).
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    // Ping every 20 s and give up when no pong comes back within 40 s: a connection that died silently (NAT
    // mapping dropped, phone switched networks) is noticed within about a minute instead of at the next
    // forced reconnect or the next failed send. The timeout is generous on purpose — while the relay flushes a
    // reconnecting device's queued frames (75 ms apart, see its /ws handler) it is not reading, hence not
    // ponging, so a short timeout could abort a long flush and start it over.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(40);

    private static readonly TimeSpan DirectoryRepublishAfter = TimeSpan.FromMinutes(30);
    private DateTimeOffset _lastDirectoryPublishUtc;

    private ClientWebSocket? _socket;
    private Task? _receiveLoopTask;
    private CancellationTokenSource? _receiveLoopCts;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event EventHandler<MessageEnvelope>? EnvelopeReceived;
    public event EventHandler<string>? PairingInviteReceived;
    public Func<string, Task>? PairingInviteProcessor { get; set; }
    public event EventHandler<string>? GroupInviteReceived;
    public event EventHandler<string>? BoardChanged;
    public event EventHandler<TransportConnectionState>? ConnectionStateChanged;

    public WebSocketMessageTransport(ISecureVaultKeyStore vault, IServiceScopeFactory scopeFactory)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task RegisterAsync(Uri endpoint, string inviteCode, string displayName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var registerUri = new Uri(ToHttpUri(endpoint), "register");
        using var response = await _httpClient.PostAsJsonAsync(
            registerUri,
            new { InviteCode = inviteCode, DisplayName = displayName },
            HttpJsonOptions,
            ct);
        response.EnsureSuccessStatusCode();

        var credential = await response.Content.ReadFromJsonAsync<DeviceCredential>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď na registraci.");

        await _vault.StoreSecretAsync(RelayDeviceVaultKeys.DeviceSecret, Encoding.UTF8.GetBytes(credential.Secret), ct);

        using var scope = _scopeFactory.CreateScope();
        var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
        var configuration = await transportSettings.GetAsync(ct) ?? new TransportEndpointConfiguration(endpoint, isAutoConnectEnabled: true);
        configuration.AssignDevice(credential.DeviceId);
        await transportSettings.SaveAsync(configuration, ct);
    }

    public async Task<Guid> RequestActivationAsync(Uri endpoint, string displayName, string email, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        using var scope = _scopeFactory.CreateScope();
        var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
        var publicKey = await messagingService.GetLocalIdentityPublicKeyAsync(ct);
        var fingerprint = ComputeKeyFingerprint(publicKey);

        var requestUri = new Uri(ToHttpUri(endpoint), "activation/request");
        using var response = await _httpClient.PostAsJsonAsync(
            requestUri,
            new { DisplayName = displayName, Email = email, KeyFingerprint = fingerprint },
            HttpJsonOptions,
            ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ActivationRequestCreated>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď na žádost o aktivaci.");

        var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
        var configuration = await transportSettings.GetAsync(ct) ?? new TransportEndpointConfiguration(endpoint, isAutoConnectEnabled: true);
        configuration.SetPendingActivationRequest(result.RequestId);
        await transportSettings.SaveAsync(configuration, ct);

        return result.RequestId;
    }

    public async Task<ActivationRequestStatus> PollActivationAsync(Uri endpoint, Guid requestId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var statusUri = new Uri(ToHttpUri(endpoint), $"activation/status/{requestId}");
        using var response = await _httpClient.GetAsync(statusUri, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ActivationStatusResult>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď na stav aktivace.");

        if (!Enum.TryParse<ActivationRequestStatus>(result.Status, ignoreCase: true, out var status))
            throw new InvalidOperationException($"Relay vrátil neznámý stav aktivace „{result.Status}“.");

        if (status == ActivationRequestStatus.Approved && result is { DeviceId: { } deviceId, Secret: { } secret })
        {
            // Same tail RegisterAsync already runs — completing it here (rather than making the
            // caller do it) is what lets PollActivationAsync stand in as a drop-in async analog of
            // RegisterAsync: "keep polling until it's not Pending" is the whole contract.
            await _vault.StoreSecretAsync(RelayDeviceVaultKeys.DeviceSecret, Encoding.UTF8.GetBytes(secret), ct);

            using var scope = _scopeFactory.CreateScope();
            var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
            var configuration = await transportSettings.GetAsync(ct) ?? new TransportEndpointConfiguration(endpoint, isAutoConnectEnabled: true);
            configuration.AssignDevice(deviceId); // also clears PendingActivationRequestId
            await transportSettings.SaveAsync(configuration, ct);
        }

        return status;
    }

    public async Task SelfRegisterAsync(Uri endpoint, string displayName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var registerUri = new Uri(ToHttpUri(endpoint), "self-register");
        using var response = await _httpClient.PostAsJsonAsync(
            registerUri,
            new { DisplayName = displayName },
            HttpJsonOptions,
            ct);
        response.EnsureSuccessStatusCode();

        var credential = await response.Content.ReadFromJsonAsync<DeviceCredential>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Relay vrátil prázdnou odpověď na self-register.");

        await _vault.StoreSecretAsync(RelayDeviceVaultKeys.DeviceSecret, Encoding.UTF8.GetBytes(credential.Secret), ct);

        using var scope = _scopeFactory.CreateScope();
        var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
        var configuration = await transportSettings.GetAsync(ct) ?? new TransportEndpointConfiguration(endpoint, isAutoConnectEnabled: true);
        configuration.AssignDevice(credential.DeviceId);
        await transportSettings.SaveAsync(configuration, ct);
    }

    /// <summary>
    /// A short, human-legible digest of a chat-identity public key for the admin to optionally read
    /// back to whoever's requesting activation (phone call, in person) before approving — not a
    /// secret, and not meant to be cryptographically unforgeable on its own (8 bytes of a SHA-256
    /// digest), just enough to catch an honest mismatch. Grouped like an SSH key fingerprint for
    /// easier reading aloud.
    /// </summary>
    private static string ComputeKeyFingerprint(byte[] publicKey)
    {
        var hash = SHA256.HashData(publicKey);
        var hex = Convert.ToHexStringLower(hash.AsSpan(0, 8));
        return string.Join(" ", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 4, 4).ToUpperInvariant()));
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        using var scope = _scopeFactory.CreateScope();
        var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
        var configuration = await transportSettings.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Zatím není zaregistrováno žádné zařízení u relay serveru.");

        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");
        var secret = Encoding.UTF8.GetString(secretBytes);

        // Already connected = a make-before-break swap (the supervisor's periodic reconnect): the live
        // connection keeps serving until the new one is authenticated, so no state change is announced yet.
        var isSwap = IsConnected;
        if (!isSwap)
            RaiseConnectionState(TransportConnectionState.Connecting);

        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = KeepAliveInterval;
        socket.Options.KeepAliveTimeout = KeepAliveTimeout;
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeout);

            await socket.ConnectAsync(new Uri(endpoint, "ws"), connectCts.Token);

            await SendFrameAsync(socket, new WireFrame { Type = "auth", DeviceId = deviceId, Secret = secret }, connectCts.Token);
            var authResult = await ReceiveFrameAsync(socket, connectCts.Token);

            // Only an actual "authResult: not successful" answer means the credentials were rejected. A
            // connection that dropped before the answer arrived (relay restart, network blip) is a transient
            // failure — treating it as "unauthorized" made the supervisor wipe the device's credentials and
            // register a brand-new device identity.
            if (authResult is null || ReferenceEquals(authResult, WireFrame.Unreadable))
                throw new IOException("Relay neodpověděl na přihlášení zařízení (spojení se přerušilo).");
            if (authResult is not { Type: "authResult", Success: true })
                throw new RelayUnauthorizedException();
        }
        catch
        {
            socket.Dispose();
            // A failed attempt must not leave the state stuck on "Connecting" — and must not announce a drop
            // when an older, still live connection is what this attempt was meant to replace.
            if (!IsConnected)
                RaiseConnectionState(TransportConnectionState.Disconnected);
            throw;
        }

        // Release whatever connection this one replaces (a dead socket the supervisor reconnected
        // over used to just leak, along with its CancellationTokenSource).
        var previousSocket = _socket;
        var previousLoopCts = _receiveLoopCts;
        _socket = socket;
        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoopTask = ReceiveLoopAsync(socket, _receiveLoopCts.Token);
        previousLoopCts?.Cancel();
        previousSocket?.Dispose();
        previousLoopCts?.Dispose();

        // Persist whichever endpoint this connect actually used (the caller may have typed a
        // different address than what RegisterAsync originally saved) and make sure auto-connect
        // stays on, so App.xaml.cs's startup auto-connect picks up the same endpoint next launch.
        configuration.SetEndpoint(endpoint, isAutoConnectEnabled: true);
        configuration.NoteConnected();
        await transportSettings.SaveAsync(configuration, ct);

        // Member directory (2026-09-06): publish this device's own identity on every successful
        // connect, not just once — cheap and idempotent (see IContactDirectoryService.PublishSelfAsync's
        // own remarks), so a later display-name change or key rotation is picked up automatically.
        // Best-effort by design, same "never let this block the actual connection" policy already
        // established for SendPairingInviteAsync — a stale/missing directory entry just means this
        // device won't show up in someone else's "New Chat" list yet, not a connection failure.
        // A periodic swap (every ~5 minutes) re-publishes at most every DirectoryRepublishAfter: the relay only needs
        // the entry to stay "recently seen" (it hides members idle for 2 days), and an ordinary reconnect after an
        // outage still publishes at once.
        if (!isSwap || DateTimeOffset.UtcNow - _lastDirectoryPublishUtc >= DirectoryRepublishAfter)
        {
            try
            {
                var directoryService = scope.ServiceProvider.GetRequiredService<IContactDirectoryService>();
                await directoryService.PublishSelfAsync(ct);
                _lastDirectoryPublishUtc = DateTimeOffset.UtcNow;
            }
            catch
            {
                // Best-effort — see the remark above.
            }
        }

        RaiseConnectionState(TransportConnectionState.Connected);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _receiveLoopCts?.Cancel();

        if (_socket is { State: WebSocketState.Open } socket)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", ct);
            }
            catch (WebSocketException)
            {
                // Best-effort close — the socket may already be gone (network drop, server restart).
            }
        }

        if (_receiveLoopTask is not null)
        {
            try { await _receiveLoopTask; }
            catch { /* the loop itself already swallows its own exceptions; this just awaits completion */ }
        }

        _socket?.Dispose();
        _socket = null;
        _receiveLoopCts?.Dispose();
        _receiveLoopCts = null;
        _receiveLoopTask = null;

        RaiseConnectionState(TransportConnectionState.Disconnected);
    }

    /// <summary>
    /// Erases the stored relay device credentials (vault secret + DB assignment) so the next
    /// <see cref="ConnectAsync"/> attempt — which would fail again with the same rejected credentials
    /// — is replaced by a fresh activation request. Called by the connection supervisor on
    /// <see cref="RelayUnauthorizedException"/>; never called on ordinary network failures.
    /// </summary>
    public async Task ClearCredentialsAsync(CancellationToken ct = default)
    {
        await _vault.RemoveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
        var config = await repo.GetAsync(ct);
        if (config is null) return;
        config.ClearRegistration();
        await repo.SaveAsync(config, ct);
    }

    public async Task SendEnvelopeAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new InvalidOperationException("Není připojeno k relay serveru.");

        using var scope = _scopeFactory.CreateScope();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var session = await sessionRepository.GetByIdAsync(envelope.SessionId, ct)
            ?? throw new ChatSessionNotFoundException(envelope.SessionId);
        var recipientDeviceId = session.PeerRelayDeviceId
            ?? throw new InvalidOperationException($"Chat session '{envelope.SessionId}' has no linked relay device id.");

        await SendFrameLockedAsync(socket, new WireFrame { Type = "send", RecipientDeviceId = recipientDeviceId, Envelope = envelope }, ct);
    }

    public async Task SendPairingInviteAsync(Guid recipientRelayDeviceId, string inviteBlob, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteBlob);
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new InvalidOperationException("Není připojeno k relay serveru.");

        await SendFrameLockedAsync(socket, new WireFrame { Type = "pairing", RecipientDeviceId = recipientRelayDeviceId, PairingInviteBlob = inviteBlob }, ct);
    }

    public async Task SendGroupInviteAsync(Guid recipientRelayDeviceId, string groupInviteBlob, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupInviteBlob);
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new InvalidOperationException("Není připojeno k relay serveru.");

        await SendFrameLockedAsync(socket, new WireFrame { Type = "group-invite", RecipientDeviceId = recipientRelayDeviceId, GroupInviteBlob = groupInviteBlob }, ct);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var frame = await ReceiveFrameAsync(socket, ct);
                if (frame is null)
                    break;
                if (ReferenceEquals(frame, WireFrame.Unreadable))
                    continue;

                // One bad frame (a database error while correlating, a throwing event handler) must not end
                // the loop: that left the socket claiming to be open while nothing read it any more, so the
                // device stayed deaf until the next forced reconnect.
                try
                {
                    await HandleFrameAsync(frame, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppLog.Error("WebSocketTransport", $"handling a relay frame ({frame.Type}) failed", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on DisconnectAsync.
        }
        catch (WebSocketException)
        {
            // Connection dropped unexpectedly — surfaced to callers via ConnectionStateChanged below, not rethrown (this loop has no caller left to observe an exception).
        }
        catch (ObjectDisposedException)
        {
            // The socket was replaced and disposed by a newer ConnectAsync while this loop was still reading it.
        }
        finally
        {
            // Only report a drop for the CURRENT connection — a loop that was replaced by a newer
            // ConnectAsync must not flip the state back to Disconnected after the new one is live.
            if (ReferenceEquals(_socket, socket))
            {
                // A loop that ended while the socket still says "open" has nobody reading it: close it for
                // real so IsConnected turns false and the supervisor reconnects at once.
                if (socket.State == WebSocketState.Open)
                {
                    try { socket.Abort(); } catch { /* best-effort */ }
                }
                RaiseConnectionState(TransportConnectionState.Disconnected);
            }
        }
    }

    private async Task HandleFrameAsync(WireFrame frame, CancellationToken ct)
    {
        if (frame is { Type: "deliver", Envelope: { } envelope })
        {
            var correlated = await CorrelateToLocalSessionAsync(frame.SenderDeviceId, envelope, ct);
            if (correlated is not null)
                EnvelopeReceived?.Invoke(this, correlated);
        }
        else if (frame is { Type: "pairing-deliver", PairingInviteBlob: { } inviteBlob })
        {
            if (PairingInviteProcessor is { } processInvite)
            {
                // Finish accepting before the next frame is read — see the property's remarks.
                // Bounded so a stuck handler can never freeze the whole receive loop.
                try { await processInvite(inviteBlob).WaitAsync(TimeSpan.FromSeconds(60), ct); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    AppLog.Error("WebSocketTransport", "processing a pairing invite failed or timed out", ex);
                }
            }
            else
            {
                PairingInviteReceived?.Invoke(this, inviteBlob);
            }
        }
        else if (frame is { Type: "group-invite-deliver", GroupInviteBlob: { } groupInviteBlob })
        {
            GroupInviteReceived?.Invoke(this, groupInviteBlob);
        }
        else if (frame is { Type: "board-changed", Board: { } board })
        {
            BoardChanged?.Invoke(this, board);
        }
    }

    /// <summary>
    /// A received <see cref="MessageEnvelope.SessionId"/> is the SENDER's own local chat-session
    /// id — meaningless here, since each side generates its <c>ChatSession.Id</c> independently
    /// and the two never match for what is conceptually "the same" conversation. The relay
    /// attaches the sender's authenticated <c>SenderDeviceId</c> to every "deliver" frame instead
    /// (trustworthy — it's relay-assigned, not client-supplied), which we use to look up OUR OWN
    /// local <see cref="ChatSession"/> for that peer (via <see cref="ChatSession.PeerRelayDeviceId"/>)
    /// and rewrite the envelope to carry our local session id before handing it onward — so
    /// <c>IMessagingService.ReceiveMessageAsync</c> and <c>ChatViewModel</c>'s session filtering see
    /// a correctly-correlated envelope and never need to know about this translation.
    /// Returns null (drop the message) if no local session is linked to that sender yet.
    ///
    /// A resync (2026-09-07 — see <c>SessionRecoveryHelper.ResyncAsync</c>) leaves the OLD, Closed
    /// session row in place alongside a brand new one for the same peer, so more than one local
    /// session can share a <see cref="ChatSession.PeerRelayDeviceId"/> at once. Prefer a non-Closed
    /// one, and among those the most recently created — otherwise an unordered
    /// <c>FirstOrDefault</c> could just as easily land back on the stale Closed row and every
    /// message from a freshly-resynced peer would keep throwing <c>ChatSessionClosedException</c>
    /// forever instead of actually recovering.
    /// </summary>
    private async Task<MessageEnvelope?> CorrelateToLocalSessionAsync(Guid? senderDeviceId, MessageEnvelope envelope, CancellationToken ct)
    {
        if (senderDeviceId is not { } senderId)
            return null;

        using var scope = _scopeFactory.CreateScope();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var sessions = await sessionRepository.GetAllAsync(ct);
        var localSession = sessions
            .Where(s => s.PeerRelayDeviceId == senderId)
            .OrderBy(s => s.State == ChatSessionState.Closed) // false (0) sorts before true (1) — non-Closed first
            .ThenByDescending(s => s.CreatedAtUtc)
            .FirstOrDefault();
        if (localSession is null)
        {
            // 2026-09-09 — the user's own suggestion, directly implemented: instead of silently
            // dropping a message from a sender we have no local session for (this exact message is
            // unrecoverable either way — there's no ratchet state to decrypt it against), look them
            // up in the relay's own directory and pair with them right now, so this doesn't keep
            // happening for every message that follows. Genuinely fire-and-forget: this method must
            // still return null for THIS envelope immediately, not block the receive loop waiting on
            // an HTTP round trip.
            _ = TryAutoPairWithUnknownSenderAsync(senderId, ct);
            return null;
        }

        return envelope with { SessionId = localSession.Id };
    }

    /// <summary>See the call site's own remarks in <see cref="CorrelateToLocalSessionAsync"/> for why this exists.</summary>
    private async Task TryAutoPairWithUnknownSenderAsync(Guid senderRelayDeviceId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var contactDirectoryService = scope.ServiceProvider.GetRequiredService<IContactDirectoryService>();
            var members = await contactDirectoryService.ListMembersAsync(ct);
            var sender = members.FirstOrDefault(m => m.RelayDeviceId == senderRelayDeviceId);
            if (sender is null)
                return; // Not (yet) published to the directory — nothing to pair with.

            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
            if (await messagingService.FindExistingSessionAsync(sender.PublicKey, ct) is not null)
                return; // A session showed up between the lookup above and here — nothing to do.

            var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
            var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
            await SessionRecoveryHelper.ResyncAsync(
                messagingService, this, transportSettings, currentUserService,
                sender.DisplayName, sender.PublicKey, sender.RelayDeviceId, ct);
        }
        catch
        {
            // Best-effort — the next message from this same sender hits this same path and tries
            // again; SessionRecoveryHelper.ResyncAsync's own cooldown keeps repeated attempts cheap.
        }
    }

    private static async Task<WireFrame?> ReceiveFrameAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var segment = new byte[8192];

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(segment, ct);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            buffer.Write(segment, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        // Null means "the connection is closed"; a frame that arrived but cannot be read is NOT that.
        if (buffer.Length == 0)
            return WireFrame.Unreadable;

        buffer.Position = 0;
        try
        {
            return await JsonSerializer.DeserializeAsync<WireFrame>(buffer, cancellationToken: ct) ?? WireFrame.Unreadable;
        }
        catch (JsonException)
        {
            return WireFrame.Unreadable;
        }
    }

    private async Task SendFrameLockedAsync(ClientWebSocket socket, WireFrame frame, CancellationToken ct)
    {
        // Bounded (see SendTimeout) — the lock wait counts too, so a queue behind a stuck send fails instead of hanging.
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sendCts.CancelAfter(SendTimeout);

        var sw = Stopwatch.StartNew();
        double waitedMs;
        try
        {
            await _sendLock.WaitAsync(sendCts.Token);
            waitedMs = sw.Elapsed.TotalMilliseconds;
            try { await SendFrameAsync(socket, frame, sendCts.Token); }
            finally { _sendLock.Release(); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && sendCts.IsCancellationRequested)
        {
            // Our own timeout, not the caller's cancellation: callers treat a cancellation as "not an error".
            AppLog.Metric("ws.send.timeout", sw.Elapsed.TotalMilliseconds, "ms", ("frame", frame.Type));
            throw new TimeoutException("Odeslání na relay trvalo příliš dlouho — spojení se obnoví.");
        }

        // Only a slow send is worth a line (2026-10-24): acks have been seen leaving ~100 s after the message
        // arrived, and this is what tells a stuck lock/socket apart from a slow handler.
        if (sw.Elapsed.TotalSeconds >= 1)
            AppLog.Metric("ws.send.slow", sw.Elapsed.TotalMilliseconds, "ms", ("frame", frame.Type), ("lockWaitMs", (int)waitedMs));
    }

    private static Task SendFrameAsync(ClientWebSocket socket, WireFrame frame, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(frame);
        return socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static Uri ToHttpUri(Uri wsEndpoint) => RelayHttp.ToHttpUri(wsEndpoint);

    private void RaiseConnectionState(TransportConnectionState state) => ConnectionStateChanged?.Invoke(this, state);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _httpClient.Dispose();
    }

    private sealed record DeviceCredential(Guid DeviceId, string Secret);
    private sealed record ActivationRequestCreated(Guid RequestId);
    private sealed record ActivationStatusResult(string Status, Guid? DeviceId, string? Secret);
}
