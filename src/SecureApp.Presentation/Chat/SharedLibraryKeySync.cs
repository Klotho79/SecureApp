using System.Collections.Concurrent;
using System.Text;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Fully automatic distribution of the community's shared library key over already-established,
/// already-authenticated E2EE pairwise sessions — the app does this entirely on its own, with zero
/// user action anywhere. Direct answer to the user's own two rounds of feedback: first the design
/// objection ("pokud je v chatu kde maji pristup uzivatele proc nemaji klic? jakou to ma logiku?" —
/// if they're already paired in a chat, why don't they have the key?), then, bluntly, that a manual
/// "resend the key" step is not acceptable at all ("nechci aby uzivatele si museli rozesilat library
/// ani jine kody... to ma udelat aplikace sama" — no app works by making users pass keys around;
/// the app must do it itself).
///
/// Two directions, both driven automatically by the connection supervisor's periodic sweep (see
/// <c>App.RunStaleSessionSweepAsync</c>), so the key propagates the moment any two paired devices are
/// online together — whichever one happens to hold it:
///   • PUSH (<see cref="OfferKeyAsync"/> / <see cref="BroadcastToAllActiveSessionsAsync"/>): a device
///     that HAS the key offers it to its paired sessions.
///   • PULL (<see cref="RequestKeyFromAllActiveSessionsAsync"/>): a device that does NOT have the key
///     asks its paired peers for it; any peer that has it answers immediately (see
///     <c>App.OnEnvelopeReceived</c>'s request handling → <see cref="OfferKeyAsync"/> with
///     <c>bypassCooldown: true</c>). The pull is what makes this robust rather than dependent on the
///     holder pushing at exactly the right moment — a keyless device drives its own key acquisition,
///     retrying every sweep until it succeeds, then stops on its own the instant it has the key.
/// <see cref="AutoSyncAsync"/> is the single entry point the sweep calls; it picks push or pull based
/// on whether this device currently has the key.
///
/// Everything rides the same ratchet a 1:1 chat message would, tagged
/// <see cref="Message.IsSystemPayload"/> so it never shows as a visible bubble (see
/// <c>ChatViewModel</c>/<c>GroupChatViewModel</c>'s own filtering). The trust boundary is already
/// exactly right: whoever completed a pairwise handshake with this device is, by definition, someone
/// this device's owner chose to pair with — the same community-membership trust the shared library
/// itself already assumes.
///
/// Best-effort and silent to the USER by design (same policy as <see cref="SessionRecoveryHelper"/>),
/// but every outcome is written to the shared diagnostics log so a "still doesn't work" report can be
/// diagnosed from the log rather than guessed at.
/// </summary>
public static class SharedLibraryKeySync
{
    /// <summary>Tags a key-carrying system payload so the receiving side's <see cref="TryParseKeyOffer"/> recognizes it.</summary>
    private const string KeyPayloadPrefix = "shared-library-key:v1:";

    /// <summary>Tags a key-REQUEST system payload — sent by a keyless device, recognized by <see cref="IsKeyRequest"/>. Deliberately disjoint from <see cref="KeyPayloadPrefix"/> (the char right after "shared-library-key" is '-' here vs ':' there), so neither prefix ever matches the other's payload.</summary>
    private const string RequestPayloadPrefix = "shared-library-key-request:v1:";

    /// <summary>
    /// Push cooldown — <see cref="OfferKeyAsync"/> is called from the periodic sweep and from
    /// chat-open, so without this the same key would be re-pushed to a session repeatedly. Harmless
    /// to correctness (the peer just re-imports the identical key) but wasteful. The PULL path (a
    /// keyless peer requesting) is what guarantees eventual delivery even when a push is on cooldown,
    /// so this can stay long. Bypassed for a direct request-response and for a freshly generated/
    /// imported key (see the <c>bypassCooldown</c> callers).
    /// </summary>
    private static readonly TimeSpan _offerCooldown = TimeSpan.FromHours(6);

    /// <summary>Pull cooldown — a keyless device asks each paired session for the key at most this often. Short, so acquisition is quick (roughly every few sweeps) while still bounding how many request rows accumulate if a key-holder is never reachable.</summary>
    private static readonly TimeSpan _requestCooldown = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastOfferedAtUtc = new();
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRequestedAtUtc = new();

    /// <summary>
    /// The single automatic entry point the connection supervisor's sweep calls (see
    /// <c>App.RunStaleSessionSweepAsync</c>). Picks the right direction with no user involvement: if
    /// this device holds the key it pushes to every active session (respecting <see cref="_offerCooldown"/>);
    /// if it does not, it asks every active session for the key (respecting <see cref="_requestCooldown"/>).
    /// </summary>
    public static async Task AutoSyncAsync(
        ISharedLibraryService sharedLibraryService,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        IChatSessionRepository chatSessionRepository,
        IDiagnosticsReporter? diagnosticsReporter = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!messageTransport.IsConnected)
                return; // Nothing can be delivered right now — the next sweep after reconnect retries.

            if (await sharedLibraryService.HasSharedKeyAsync(ct))
            {
                // PRIMARY (2026-09-11): relay-mediated escrow — wrap the key for every member and
                // leave it on the relay. Robust regardless of session health or who's online, unlike
                // the ratchet offer below (kept as a secondary path for a freshly-paired session).
                await sharedLibraryService.PublishWrappedKeyForMembersAsync(ct);

                foreach (var session in await GetActiveSessionsAsync(chatSessionRepository, ct))
                    await OfferKeyAsync(sharedLibraryService, messagingService, messageTransport, session.Id, diagnosticsReporter, bypassCooldown: false, ct: ct);
            }
            else
            {
                // PRIMARY: pull our escrowed wrapped key from the relay and unwrap it locally. If it
                // succeeds we're done; otherwise fall back to asking paired peers over the ratchet.
                if (await sharedLibraryService.TryImportWrappedKeyAsync(ct))
                {
                    _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Info, "Klíč sdílené knihovny získán z úložiště na relay a naimportován.", nameof(SharedLibraryKeySync), ct: ct));
                    return;
                }

                await RequestKeyFromAllActiveSessionsAsync(sharedLibraryService, messagingService, messageTransport, chatSessionRepository, diagnosticsReporter, ct);
            }
        }
        catch (Exception ex)
        {
            _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Warning, "Automatická synchronizace klíče sdílené knihovny narazila na chybu (zkusí se znovu při dalším průchodu).", nameof(SharedLibraryKeySync), ex, ct));
        }
    }

    /// <summary>
    /// PUSH one session. Called automatically from every pairing-completion site
    /// (<c>NewChatViewModel</c>, <c>App.OnPairingInviteReceived</c>/<c>OnGroupInviteReceived</c>,
    /// <c>GroupChatViewModel</c>/<c>NewGroupViewModel</c>), from <c>ChatViewModel.LoadAsync</c>, from
    /// the periodic <see cref="AutoSyncAsync"/>, and — with <paramref name="bypassCooldown"/> true —
    /// as the direct answer to an incoming key request. A no-op whenever this device has no key yet.
    /// </summary>
    public static async Task OfferKeyAsync(
        ISharedLibraryService sharedLibraryService,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        Guid sessionId,
        IDiagnosticsReporter? diagnosticsReporter = null,
        bool bypassCooldown = false,
        CancellationToken ct = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (!bypassCooldown)
            {
                var lastOffered = _lastOfferedAtUtc.GetOrAdd(sessionId, DateTimeOffset.MinValue);
                if (now - lastOffered < _offerCooldown)
                    return; // Pushed to this session recently — the pull path still covers eventual delivery.
            }

            if (!await sharedLibraryService.HasSharedKeyAsync(ct))
                return;

            _lastOfferedAtUtc[sessionId] = now;

            var keyBlob = await sharedLibraryService.ExportSharedKeyAsync(ct);
            var plaintext = Encoding.UTF8.GetBytes(KeyPayloadPrefix + keyBlob);

            var (_, envelope) = await messagingService.SendMessageAsync(sessionId, plaintext, isSystemPayload: true, ct: ct);

            if (messageTransport.IsConnected)
            {
                await messageTransport.SendEnvelopeAsync(envelope, ct);
                _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Info, $"Klíč sdílené knihovny nabídnut přes session {sessionId} (doručeno živě).", nameof(SharedLibraryKeySync), ct: ct));
            }
            else
            {
                _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Warning, $"Klíč sdílené knihovny uložen pro session {sessionId}, ale relay není připojen — doručí se při příštím spojení.", nameof(SharedLibraryKeySync), ct: ct));
            }
        }
        catch (Exception ex)
        {
            _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Error, $"Nabídka klíče sdílené knihovny pro session {sessionId} selhala.", nameof(SharedLibraryKeySync), ex, ct));
        }
    }

    /// <summary>
    /// PUSH to every active session at once, bypassing the per-session cooldown — used automatically
    /// the instant a key is generated or imported (see <c>SettingsViewModel</c>), so a brand-new key
    /// reaches everyone this device is already paired with immediately rather than waiting for the
    /// next sweep. Returns how many sessions were offered to.
    /// </summary>
    public static async Task<int> BroadcastToAllActiveSessionsAsync(
        ISharedLibraryService sharedLibraryService,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        IChatSessionRepository chatSessionRepository,
        IDiagnosticsReporter? diagnosticsReporter = null,
        CancellationToken ct = default)
    {
        var activeSessions = await GetActiveSessionsAsync(chatSessionRepository, ct);
        foreach (var session in activeSessions)
            await OfferKeyAsync(sharedLibraryService, messagingService, messageTransport, session.Id, diagnosticsReporter, bypassCooldown: true, ct: ct);
        return activeSessions.Count;
    }

    /// <summary>
    /// PULL: a device without the key asks every active session for it. A no-op the moment this
    /// device actually has the key (nothing to pull), so it stops on its own — no explicit "stop
    /// requesting" bookkeeping needed. Any peer that has the key answers via
    /// <c>App.OnEnvelopeReceived</c>'s request handling.
    /// </summary>
    public static async Task RequestKeyFromAllActiveSessionsAsync(
        ISharedLibraryService sharedLibraryService,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        IChatSessionRepository chatSessionRepository,
        IDiagnosticsReporter? diagnosticsReporter = null,
        CancellationToken ct = default)
    {
        if (await sharedLibraryService.HasSharedKeyAsync(ct))
            return; // Already have it — nothing to ask for.

        foreach (var session in await GetActiveSessionsAsync(chatSessionRepository, ct))
        {
            var now = DateTimeOffset.UtcNow;
            var lastRequested = _lastRequestedAtUtc.GetOrAdd(session.Id, DateTimeOffset.MinValue);
            if (now - lastRequested < _requestCooldown)
                continue;
            _lastRequestedAtUtc[session.Id] = now;

            try
            {
                var plaintext = Encoding.UTF8.GetBytes(RequestPayloadPrefix);
                var (_, envelope) = await messagingService.SendMessageAsync(session.Id, plaintext, isSystemPayload: true, ct: ct);
                if (messageTransport.IsConnected)
                {
                    await messageTransport.SendEnvelopeAsync(envelope, ct);
                    _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Info, $"Vyžádán klíč sdílené knihovny přes session {session.Id} (toto zařízení klíč zatím nemá).", nameof(SharedLibraryKeySync), ct: ct));
                }
            }
            catch (Exception ex)
            {
                _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Warning, $"Žádost o klíč sdílené knihovny přes session {session.Id} selhala.", nameof(SharedLibraryKeySync), ex, ct));
            }
        }
    }

    /// <summary>Receiving side: is this decrypted system payload a key offer (and if so, extract the blob)?</summary>
    public static bool TryParseKeyOffer(byte[] plaintext, out string keyBlob)
    {
        var text = SafeDecode(plaintext);
        if (text is not null && text.StartsWith(KeyPayloadPrefix, StringComparison.Ordinal))
        {
            keyBlob = text[KeyPayloadPrefix.Length..];
            return true;
        }
        keyBlob = string.Empty;
        return false;
    }

    /// <summary>Receiving side: is this decrypted system payload a request for the key? If so, and this device has the key, the caller answers with <see cref="OfferKeyAsync"/> (bypassing the cooldown).</summary>
    public static bool IsKeyRequest(byte[] plaintext)
    {
        var text = SafeDecode(plaintext);
        return text is not null && text.StartsWith(RequestPayloadPrefix, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<ChatSession>> GetActiveSessionsAsync(IChatSessionRepository chatSessionRepository, CancellationToken ct)
    {
        var sessions = await chatSessionRepository.GetAllAsync(ct);
        return sessions.Where(s => s.State == ChatSessionState.Active).ToList();
    }

    private static string? SafeDecode(byte[] plaintext)
    {
        try { return Encoding.UTF8.GetString(plaintext); }
        catch { return null; }
    }
}
