using System.Collections.Concurrent;
using System.Text;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Auto-distributes the community's shared library key over an already-established, already-
/// authenticated E2EE pairwise session (2026-09-10) — direct answer to the user's own architectural
/// objection: "pokud je v chatu kde maji pristup uzivatele proc nemaji klic? jakou to ma logiku?"
/// (if they're already trusted enough to be paired in a chat, why don't they have the key — what's
/// the logic in that?). Before this, a freshly-onboarded device had no shared library key until
/// someone manually copy/pasted <c>ISharedLibraryService.ExportSharedKeyAsync</c>'s blob through
/// Settings — a real, reported "nepodařilo se otevřít soubor, není žádný sdílený library key"
/// failure with no actual bug in the crypto, just a missing distribution path.
///
/// 2026-09-11 follow-up (still reported broken after the first pass): the per-pairing/per-chat-open
/// triggers below only fire the offer from whichever device the user happens to be poking at, which
/// turned out too passive — a device that generates/imports a key has no reason for the USER to open
/// every existing chat afterward, and the receiving device has no visibility into whether anything
/// was ever even attempted. Two fixes: <see cref="BroadcastToAllActiveSessionsAsync"/> pushes the key
/// out to every already-paired session the moment it's generated/imported (see
/// <c>SettingsViewModel</c>'s own call sites) instead of waiting for an unrelated chat-open event, and
/// every path here now takes an optional <see cref="IDiagnosticsReporter"/> so a failure is actually
/// visible in the shared diagnostics log instead of silently vanishing into a bare catch.
///
/// Rides the same ratchet a 1:1 chat message would, tagged <see cref="Message.IsSystemPayload"/>
/// so it never shows up as a visible bubble (see <c>ChatViewModel</c>/<c>GroupChatViewModel</c>'s own
/// filtering) — the trust boundary is already exactly right: whoever completed a pairwise handshake
/// with this device is, by definition, someone this device's owner chose to pair with, which is
/// precisely the same community-membership trust the shared library itself already assumes.
///
/// Best-effort and silent-to-the-USER by design, same policy as <see cref="SessionRecoveryHelper"/>
/// and <c>ChatViewModel.SendAsync</c>'s own live-send failure handling — offering the key is a bonus
/// on top of an otherwise-complete pairing, never something that should surface an error or block
/// opening the chat if it fails (peer offline, this device has no key of its own yet, etc.). "Silent
/// to the user" no longer means "silent to the diagnostics log", though — see the note above.
/// </summary>
public static class SharedLibraryKeySync
{
    /// <summary>
    /// Tags the plaintext so the receiving side's <see cref="TryParseKeyOffer"/> can recognize this
    /// specific system payload — <see cref="Message.IsSystemPayload"/> only says "not a
    /// visible chat message", not which kind of internal payload this is, and more system payload
    /// kinds may show up here later.
    /// </summary>
    private const string PayloadPrefix = "shared-library-key:v1:";

    /// <summary>
    /// Per-session cooldown (2026-09-10) — <see cref="OfferKeyAsync"/> is also called from
    /// <c>ChatViewModel.LoadAsync</c> now (every time an EXISTING chat thread is simply opened, not
    /// just at pairing time — see that call site's own remarks for why: an already-paired session
    /// that predates this whole mechanism never gets a fresh pairing event to hang the offer off
    /// of). Without a cooldown, reopening the same chat repeatedly would re-send the same system
    /// payload every time — harmless to correctness (the peer just re-imports the identical key) but
    /// wasteful. <paramref name="bypassCooldown"/> on <see cref="OfferKeyAsync"/> skips this
    /// deliberately for <see cref="BroadcastToAllActiveSessionsAsync"/> — a manual "resend to
    /// everyone" action the user explicitly asked for should never be silently swallowed by a timer.
    /// </summary>
    private static readonly TimeSpan _cooldown = TimeSpan.FromHours(6);

    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastOfferedAtUtc = new();

    /// <summary>
    /// Called from every pairing-completion site (initiator and responder alike, group fan-out
    /// included) the moment a session becomes usable — see call sites in <c>NewChatViewModel</c>,
    /// <c>App.OnPairingInviteReceived</c>/<c>OnGroupInviteReceived</c>, and
    /// <c>GroupChatViewModel.BroadcastMembershipAsync</c> — plus <c>ChatViewModel.LoadAsync</c> for
    /// the already-paired-before-this-existed case. A no-op (not an error) whenever this device has
    /// no key of its own yet — nothing to offer; the peer who eventually generates or imports one
    /// will offer it back over the same still-open session later.
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
                if (now - lastOffered < _cooldown)
                    return; // Already offered this session recently — see the cooldown's own remarks.
            }

            if (!await sharedLibraryService.HasSharedKeyAsync(ct))
                return;

            _lastOfferedAtUtc[sessionId] = now;

            var keyBlob = await sharedLibraryService.ExportSharedKeyAsync(ct);
            var plaintext = Encoding.UTF8.GetBytes(PayloadPrefix + keyBlob);

            var (_, envelope) = await messagingService.SendMessageAsync(sessionId, plaintext, isSystemPayload: true, ct: ct);

            // Best-effort live delivery only, same "stays Pending, never surfaced" policy every
            // other live-send path in this app already uses — a peer who's offline right now simply
            // doesn't get this particular offer. Logged either way (2026-09-11) so the diagnostics
            // log can actually distinguish "never attempted" from "sent locally but peer was
            // offline" from "delivered live" — the three outcomes that were previously
            // indistinguishable from outside a debugger.
            if (messageTransport.IsConnected)
            {
                await messageTransport.SendEnvelopeAsync(envelope, ct);
                _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Info, $"Klíč sdílené knihovny nabídnut přes session {sessionId} (doručeno živě).", nameof(SharedLibraryKeySync), ct: ct));
            }
            else
            {
                _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Warning, $"Klíč sdílené knihovny uložen pro session {sessionId}, ale relay není připojen — doručí se, až se peer/toto zařízení příště spojí.", nameof(SharedLibraryKeySync), ct: ct));
            }
        }
        catch (Exception ex)
        {
            // Never let a key-offer failure block or surface an error on the pairing flow that
            // triggered it — same reasoning as every other best-effort helper in this file's
            // neighborhood (SessionRecoveryHelper, App.OnGroupInviteReceived's per-member loop).
            // Logged (2026-09-11), not swallowed silently — this exact kind of "why doesn't it just
            // work" report is what the shared diagnostics log exists for.
            _ = (diagnosticsReporter?.ReportAsync(DiagnosticLogLevel.Error, $"Nabídka klíče sdílené knihovny pro session {sessionId} selhala.", nameof(SharedLibraryKeySync), ex, ct));
        }
    }

    /// <summary>
    /// Explicit "resend to everyone I'm already paired with" (2026-09-11) — called from
    /// <c>SettingsViewModel</c> right after a key is generated/imported, and from its own "Rozeslat
    /// klíč" button for a manual re-push at any later time (the user's own ask: distribution
    /// shouldn't depend on happening to reopen the right chat). Bypasses the per-session cooldown —
    /// a deliberate, explicit user/system action should never be silently dropped by a rate limiter
    /// meant for incidental chat-open traffic. Returns how many sessions were offered to, for the
    /// caller to surface as a status message.
    /// </summary>
    public static async Task<int> BroadcastToAllActiveSessionsAsync(
        ISharedLibraryService sharedLibraryService,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        IChatSessionRepository chatSessionRepository,
        IDiagnosticsReporter? diagnosticsReporter = null,
        CancellationToken ct = default)
    {
        var sessions = await chatSessionRepository.GetAllAsync(ct);
        var activeSessions = sessions.Where(s => s.State == ChatSessionState.Active).ToList();

        foreach (var session in activeSessions)
            await OfferKeyAsync(sharedLibraryService, messagingService, messageTransport, session.Id, diagnosticsReporter, bypassCooldown: true, ct: ct);

        return activeSessions.Count;
    }

    /// <summary>
    /// Receiving side: called from <c>App.OnEnvelopeReceived</c> after a successful decrypt, only
    /// when <c>envelope.IsSystemPayload</c> is set. Returns false for plaintext that doesn't carry
    /// this specific system payload's tag (forward-compatible with other system payload kinds this
    /// mechanism might carry later) — never throws on unrecognized content.
    /// </summary>
    public static bool TryParseKeyOffer(byte[] plaintext, out string keyBlob)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            keyBlob = string.Empty;
            return false;
        }

        if (text.StartsWith(PayloadPrefix, StringComparison.Ordinal))
        {
            keyBlob = text[PayloadPrefix.Length..];
            return true;
        }

        keyBlob = string.Empty;
        return false;
    }
}
