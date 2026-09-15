using System.Collections.Concurrent;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// One-sided pairwise session resync (2026-09-07): closes any existing session with a peer, mints a
/// fresh Double Ratchet handshake, and sends the resulting invite over the relay — as a single,
/// self-sufficient action triggered from ONE device only.
///
/// Replaces the earlier "press the same reset button on both devices" recovery path, which turned
/// out to be a dead end live: nothing ever re-paired afterward (no UI offered a way to re-invite an
/// EXISTING group member once its session was closed — <c>ConfirmAddMembersAsync</c> only offers
/// members not already in the group), and even on the rare path where a fresh invite did go out,
/// <see cref="App.OnPairingInviteReceived"/> silently ignored it whenever the receiving side still
/// had an (unknowingly broken) session on file for that peer. Both are fixed together: this helper
/// always follows a close with an immediate new handshake + invite, and
/// <see cref="App.OnPairingInviteReceived"/> now always accepts a fresh invite from an already-paired
/// peer — closing its own stale copy first — instead of ignoring it. So a single tap (manual "↺"
/// reset) or an automatic trigger (see the chat/group ViewModels' own <c>TryAutoHealAsync</c>, fired
/// straight off a genuine decrypt failure with no button press at all) is enough on its own; the
/// other person's device needs to do nothing.
/// </summary>
public static class SessionRecoveryHelper
{
    /// <summary>
    /// How long one resync gets to actually settle (its invite reach the peer, the peer accept it)
    /// before another trigger for the SAME peer is allowed to run at all (2026-09-07 — a real bug
    /// caught live: with several independent triggers for the same broken pairing — the reactive
    /// per-message <c>TryAutoHealAsync</c>, the opportunistic per-send background resync, the
    /// on-open <c>ResyncMissingMembersAsync</c> sweep, and the periodic connection-supervisor sweep —
    /// firing close together (e.g. a burst of test messages) each tore down the session the previous
    /// one had JUST created, before its invite could even land, so the pairing could never actually
    /// stabilize and every single message kept failing the same way forever. This is a simple,
    /// process-wide cooldown, not per-caller: whichever trigger gets there first for a given peer
    /// wins, and every other trigger for that same peer is a silent no-op until the cooldown lapses.
    /// </summary>
    private static readonly TimeSpan _cooldown = TimeSpan.FromSeconds(15);

    private static readonly ConcurrentDictionary<string, DateTimeOffset> _lastResyncAttemptUtc = new();

    public static async Task ResyncAsync(
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        ITransportSettingsRepository transportSettingsRepository,
        ICurrentUserService currentUserService,
        string peerDisplayName,
        byte[] peerPublicKey,
        Guid peerRelayDeviceId,
        CancellationToken ct = default)
        => await ResyncAsync(messagingService, messageTransport, transportSettingsRepository, currentUserService, null, null, peerDisplayName, peerPublicKey, peerRelayDeviceId, ct);

    /// <param name="messageRepository">
    /// Optional (2026-09-15) — when given together with <paramref name="sessionRepository"/>, this
    /// peer's ENTIRE session history (every prior Closed session, not just the one just superseded —
    /// see <see cref="ReassignAllHistoryAsync"/>'s own remarks for why a single hop isn't enough) is
    /// re-parented onto the fresh session id. Without it, closed sessions' history stays stranded
    /// under their old ids, the pre-existing behavior. Deliberately does NOT also resend this device's
    /// own undelivered messages on the old session — unlike the ACCEPTING side (see
    /// <c>App.OnPairingInviteReceived</c>), the peer hasn't necessarily processed this device's fresh
    /// invite yet at this point, so an ordinary message sent immediately after could race ahead of it
    /// and arrive at a peer with no session to decrypt it against yet.
    /// </param>
    /// <param name="sessionRepository">Optional, paired with <paramref name="messageRepository"/> — see above.</param>
    public static async Task ResyncAsync(
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        ITransportSettingsRepository transportSettingsRepository,
        ICurrentUserService currentUserService,
        IMessageRepository? messageRepository,
        IChatSessionRepository? sessionRepository,
        string peerDisplayName,
        byte[] peerPublicKey,
        Guid peerRelayDeviceId,
        CancellationToken ct = default)
    {
        var peerKeyHex = Convert.ToHexStringLower(peerPublicKey);
        var now = DateTimeOffset.UtcNow;
        var lastAttempt = _lastResyncAttemptUtc.GetOrAdd(peerKeyHex, DateTimeOffset.MinValue);
        if (now - lastAttempt < _cooldown)
        {
            AppLog.Event("session.resync.skip", ("peer", peerDisplayName), ("reason", "cooldown"));
            return; // Another trigger already resynced this same peer moments ago — let it settle.
        }
        _lastResyncAttemptUtc[peerKeyHex] = now;
        AppLog.Event("session.resync.start", ("peer", peerDisplayName), ("peerDevice", peerRelayDeviceId));

        var existing = await messagingService.FindExistingSessionAsync(peerPublicKey, ct);
        if (existing is not null)
            await messagingService.CloseSessionAsync(existing.Id, ct);

        var configuration = await transportSettingsRepository.GetAsync(ct);
        var ownDeviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var ownPublicKey = await messagingService.GetLocalIdentityPublicKeyAsync(ct);
        await currentUserService.InitializeAsync(ct);

        var (newSession, handshakeCipherText) = await messagingService.CreateSessionAsync(peerDisplayName, peerPublicKey, peerRelayDeviceId, ct);
        var invite = new ChatInviteBlob(currentUserService.Current.DisplayName, ownPublicKey, ownDeviceId, handshakeCipherText);

        if (messageRepository is not null && sessionRepository is not null)
        {
            var migrated = await ReassignAllHistoryAsync(sessionRepository, messageRepository, peerPublicKey, newSession.Id, ct);
            if (migrated > 0)
                AppLog.Event("session.resync.history-migrated", ("peer", peerDisplayName), ("sessions", migrated), ("to", newSession.Id));
        }

        if (messageTransport.IsConnected)
        {
            await messageTransport.SendPairingInviteAsync(peerRelayDeviceId, ContactCardCodec.Encode(invite), ct);
            AppLog.Event("session.resync.invite-sent", ("peer", peerDisplayName), ("peerDevice", peerRelayDeviceId));
        }
        else
        {
            AppLog.Event("session.resync.invite-queued", ("peer", peerDisplayName), ("reason", "relay-not-connected"));
        }
        // Not connected right now: the session stays PendingHandshake locally, same "no error
        // surfaced" policy this app already uses elsewhere. It isn't retried automatically the
        // instant the relay reconnects — App.AutoConnectRelayAsync's periodic health check (below)
        // is what eventually picks this back up.
    }

    /// <summary>
    /// Re-parents EVERY prior session's message history for this peer onto the fresh session
    /// (2026-09-15) — not just the one session that was JUST superseded. A peer can accumulate several
    /// generations of Closed sessions across repeated resyncs (each one itself superseding the one
    /// before it); reassigning only the immediately-preceding session leaves anything stranded on an
    /// EARLIER one permanently orphaned — including messages stuck there from before this migration
    /// mechanism even existed. Walks every session this device has EVER had with this peer (any state)
    /// via <see cref="IChatSessionRepository.GetAllByPeerPublicKeyAsync"/> and migrates each one's
    /// messages onto <paramref name="newSessionId"/>; a session with no messages is a cheap no-op.
    /// Returns how many OTHER sessions were found (0 = nothing to migrate, not an error).
    /// </summary>
    public static async Task<int> ReassignAllHistoryAsync(
        IChatSessionRepository sessionRepository,
        IMessageRepository messageRepository,
        byte[] peerPublicKey,
        Guid newSessionId,
        CancellationToken ct = default)
    {
        var allForPeer = await sessionRepository.GetAllByPeerPublicKeyAsync(peerPublicKey, ct);
        var others = allForPeer.Where(s => s.Id != newSessionId).ToList();
        foreach (var old in others)
            await messageRepository.ReassignSessionAsync(old.Id, newSessionId, ct);
        return others.Count;
    }

    /// <summary>
    /// Re-sends this device's own not-yet-delivered outbound messages on <paramref name="sessionId"/>
    /// (2026-09-15) — call right after a session was (re)established well enough to send on, so
    /// anything that got stuck under whatever session this one replaced actually reaches the peer
    /// instead of quietly staying Pending forever. Each candidate row is decrypted from local storage
    /// (see <c>Message.Payload</c>'s own remarks — it's vault-encrypted, not the one-shot ratchet
    /// ciphertext, so it's always redecryptable), its stale copy deleted, and a fresh
    /// <see cref="IMessagingService.SendMessageAsync"/> issues a brand-new envelope over the current
    /// ratchet — reusing the OLD ciphertext is not an option, that key was consumed and is gone the
    /// moment the old session closed (forward secrecy). Best-effort per message: one failing never
    /// stops the rest, and a transport failure just leaves the fresh copy Pending like any other send.
    /// Call this ONLY once the recipient side is known ready (e.g. right after this device itself
    /// accepted a fresh session) — see <see cref="ResyncAsync"/>'s own remarks on why the INITIATING
    /// side of a resync does not do this.
    /// </summary>
    public static async Task ResendUndeliveredAsync(
        IMessagingService messagingService,
        IMessageRepository messageRepository,
        IMessageTransport messageTransport,
        Guid sessionId,
        CancellationToken ct = default)
    {
        var rows = await messageRepository.GetBySessionAsync(sessionId, ct: ct);
        var undelivered = rows.Where(m =>
            m.Direction == MessageDirection.Outbound &&
            !m.IsSystemPayload &&
            m.Status is MessageStatus.Pending or MessageStatus.Sent or MessageStatus.Failed);

        foreach (var old in undelivered)
        {
            try
            {
                var plaintext = await messagingService.DecryptMessageAsync(old.Id, ct);
                await messageRepository.DeleteAsync(old.Id, ct);
                var (_, envelope) = await messagingService.SendMessageAsync(
                    sessionId, plaintext,
                    attachmentDocumentId: old.AttachmentDocumentId,
                    attachmentLibraryFileId: old.AttachmentLibraryFileId,
                    attachmentFileName: old.AttachmentFileName,
                    groupChatId: old.GroupChatId,
                    groupMessageId: old.GroupMessageId,
                    ct: ct);
                if (messageTransport.IsConnected)
                    await messageTransport.SendEnvelopeAsync(envelope, ct);
                AppLog.Event("session.resend", ("session", sessionId));
            }
            catch (Exception ex)
            {
                // Best-effort — one message failing to resend must not block the rest, and the old row
                // is already gone either way (either resent successfully, or lost the same way it would
                // have been anyway; leaving a half-decrypted duplicate around would be worse).
                AppLog.Error("SessionRecovery.Resend", "failed to resend an undelivered message after resync", ex);
            }
        }
    }
}
