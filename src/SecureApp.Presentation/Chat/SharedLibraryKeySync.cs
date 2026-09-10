using System.Collections.Concurrent;
using System.Text;
using SecureApp.Domain.Entities;
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
/// Rides the same ratchet a 1:1 chat message would, tagged <see cref="Message.IsSystemPayload"/>
/// so it never shows up as a visible bubble (see <c>ChatViewModel</c>/<c>GroupChatViewModel</c>'s own
/// filtering) — the trust boundary is already exactly right: whoever completed a pairwise handshake
/// with this device is, by definition, someone this device's owner chose to pair with, which is
/// precisely the same community-membership trust the shared library itself already assumes.
///
/// Best-effort and silent by design, same policy as <see cref="SessionRecoveryHelper"/> and
/// <c>ChatViewModel.SendAsync</c>'s own live-send failure handling — offering the key is a bonus on
/// top of an otherwise-complete pairing, never something that should surface an error or block
/// opening the chat if it fails (peer offline, this device has no key of its own yet, etc.).
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
    /// wasteful. Mirrors <see cref="SessionRecoveryHelper"/>'s own process-wide cooldown dictionary
    /// shape, just longer-lived: this isn't racing concurrent triggers, only rate-limiting a single
    /// user's normal open-chat browsing.
    /// </summary>
    private static readonly TimeSpan _cooldown = TimeSpan.FromHours(6);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> _lastOfferedAtUtc = new();

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
        CancellationToken ct = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var lastOffered = _lastOfferedAtUtc.GetOrAdd(sessionId, DateTimeOffset.MinValue);
            if (now - lastOffered < _cooldown)
                return; // Already offered this session recently — see the cooldown's own remarks.

            if (!await sharedLibraryService.HasSharedKeyAsync(ct))
                return;

            _lastOfferedAtUtc[sessionId] = now;

            var keyBlob = await sharedLibraryService.ExportSharedKeyAsync(ct);
            var plaintext = Encoding.UTF8.GetBytes(PayloadPrefix + keyBlob);

            var (_, envelope) = await messagingService.SendMessageAsync(sessionId, plaintext, isSystemPayload: true, ct: ct);

            // Best-effort live delivery only, same "stays Pending, never surfaced" policy every
            // other live-send path in this app already uses — a peer who's offline right now simply
            // doesn't get this particular offer; nothing here retries it later, but the NEXT time
            // either side re-pairs (a resync, a fresh group snapshot, reopening the app) calls this
            // helper again, so an eventually-reconnected peer isn't permanently missed either.
            if (messageTransport.IsConnected)
                await messageTransport.SendEnvelopeAsync(envelope, ct);
        }
        catch
        {
            // Never let a key-offer failure block or surface an error on the pairing flow that
            // triggered it — same reasoning as every other best-effort helper in this file's
            // neighborhood (SessionRecoveryHelper, App.OnGroupInviteReceived's per-member loop).
        }
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
