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

    private ClientWebSocket? _socket;
    private Task? _receiveLoopTask;
    private CancellationTokenSource? _receiveLoopCts;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event EventHandler<MessageEnvelope>? EnvelopeReceived;
    public event EventHandler<string>? PairingInviteReceived;
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
            ?? throw new InvalidOperationException("Relay returned an empty registration response.");

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
            ?? throw new InvalidOperationException("Relay returned an empty activation-request response.");

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
            ?? throw new InvalidOperationException("Relay returned an empty activation-status response.");

        if (!Enum.TryParse<ActivationRequestStatus>(result.Status, ignoreCase: true, out var status))
            throw new InvalidOperationException($"Relay returned an unrecognized activation status '{result.Status}'.");

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
            ?? throw new InvalidOperationException("No device is registered with a relay yet — call RegisterAsync first.");

        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("Relay device secret is missing from the vault.");
        var secret = Encoding.UTF8.GetString(secretBytes);

        RaiseConnectionState(TransportConnectionState.Connecting);

        var socket = new ClientWebSocket();
        var wsUri = new Uri(endpoint, "ws");
        await socket.ConnectAsync(wsUri, ct);

        await SendFrameAsync(socket, new WireFrame { Type = "auth", DeviceId = deviceId, Secret = secret }, ct);
        var authResult = await ReceiveFrameAsync(socket, ct);
        if (authResult is not { Type: "authResult", Success: true })
        {
            socket.Dispose();
            RaiseConnectionState(TransportConnectionState.Disconnected);
            throw new InvalidOperationException("Relay authentication failed.");
        }

        _socket = socket;
        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoopTask = ReceiveLoopAsync(socket, _receiveLoopCts.Token);

        // Persist whichever endpoint this connect actually used (the caller may have typed a
        // different address than what RegisterAsync originally saved) and make sure auto-connect
        // stays on, so App.xaml.cs's startup auto-connect picks up the same endpoint next launch.
        configuration.SetEndpoint(endpoint, isAutoConnectEnabled: true);
        configuration.NoteConnected();
        await transportSettings.SaveAsync(configuration, ct);

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

    public async Task SendEnvelopeAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new InvalidOperationException("Not connected to a relay.");

        using var scope = _scopeFactory.CreateScope();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var session = await sessionRepository.GetByIdAsync(envelope.SessionId, ct)
            ?? throw new ChatSessionNotFoundException(envelope.SessionId);
        var recipientDeviceId = session.PeerRelayDeviceId
            ?? throw new InvalidOperationException($"Chat session '{envelope.SessionId}' has no linked relay device id.");

        await SendFrameAsync(socket, new WireFrame { Type = "send", RecipientDeviceId = recipientDeviceId, Envelope = envelope }, ct);
    }

    public async Task SendPairingInviteAsync(Guid recipientRelayDeviceId, string inviteBlob, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteBlob);
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new InvalidOperationException("Not connected to a relay.");

        await SendFrameAsync(socket, new WireFrame { Type = "pairing", RecipientDeviceId = recipientRelayDeviceId, PairingInviteBlob = inviteBlob }, ct);
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

                if (frame is { Type: "deliver", Envelope: { } envelope })
                {
                    var correlated = await CorrelateToLocalSessionAsync(frame.SenderDeviceId, envelope, ct);
                    if (correlated is not null)
                        EnvelopeReceived?.Invoke(this, correlated);
                }
                else if (frame is { Type: "pairing-deliver", PairingInviteBlob: { } inviteBlob })
                {
                    PairingInviteReceived?.Invoke(this, inviteBlob);
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
        finally
        {
            RaiseConnectionState(TransportConnectionState.Disconnected);
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
    /// </summary>
    private async Task<MessageEnvelope?> CorrelateToLocalSessionAsync(Guid? senderDeviceId, MessageEnvelope envelope, CancellationToken ct)
    {
        if (senderDeviceId is not { } senderId)
            return null;

        using var scope = _scopeFactory.CreateScope();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var sessions = await sessionRepository.GetAllAsync(ct);
        var localSession = sessions.FirstOrDefault(s => s.PeerRelayDeviceId == senderId);
        if (localSession is null)
            return null;

        return envelope with { SessionId = localSession.Id };
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

        if (buffer.Length == 0)
            return null;

        buffer.Position = 0;
        try
        {
            return await JsonSerializer.DeserializeAsync<WireFrame>(buffer, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task SendFrameAsync(ClientWebSocket socket, WireFrame frame, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(frame);
        return socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static Uri ToHttpUri(Uri wsEndpoint)
    {
        var scheme = wsEndpoint.Scheme switch
        {
            "ws" => "http",
            "wss" => "https",
            _ => wsEndpoint.Scheme
        };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

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
