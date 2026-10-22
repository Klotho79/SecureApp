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
    ///
    /// 2026-10-04 — raised from 15 seconds after finding a REAL instance live via the shared
    /// diagnostics log: a peer the receiving side had marked removed (so every fresh invite just gets
    /// held for that user's explicit consent, never auto-accepted — see RemovedPeersStore/
    /// PendingInvitesStore) kept getting a brand new handshake-close-then-recreate from this device's
    /// own App.RunStaleSessionSweepAsync every single 3-minute sweep tick, for 16+ hours straight,
    /// because 15 seconds is nowhere near that sweep's own interval — the OLD cooldown only ever
    /// protected against several triggers firing within the same few seconds, never against the
    /// periodic sweep finding the exact same still-unresolved peer "stale" again next cycle and
    /// next cycle after that. Resyncing more aggressively can't make the OTHER side's consent-gate
    /// resolve any faster — the only thing the old cooldown bought here was pure noise (a fresh
    /// handshake + full history re-migration, every 3 minutes, for nothing). 20 minutes is still far
    /// short of ever delaying a genuinely NEW break from healing at human-noticeable speed, but long
    /// enough that a peer stuck exactly like this one generates a small, countable number of attempts
    /// instead of an unbounded one.
    /// </summary>
    private static readonly TimeSpan _cooldown = TimeSpan.FromMinutes(20);

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
        => await ResyncAsync(messagingService, messageTransport, transportSettingsRepository, currentUserService, null, null, peerDisplayName, peerPublicKey, peerRelayDeviceId, migratingFromPublicKey: null, ct: ct);

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
    /// <param name="migratingFromPublicKey">
    /// Identity-migration case (2026-09-19, see <see cref="PeerIdentityReconciler"/>) — when a peer
    /// re-registered under a brand new <paramref name="peerPublicKey"/> entirely, their OLD sessions'
    /// history lives under their OLD key, not the new one. Without this, <see cref="ReassignAllHistoryAsync"/>
    /// would look for history under <paramref name="peerPublicKey"/> itself and (correctly, for the
    /// plain same-key resync case this parameter defaults to) find nothing new to migrate. Null for
    /// every pre-existing call site — an ordinary resync's peer key never changes.
    /// </param>
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
        byte[]? migratingFromPublicKey = null,
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
            var historySourceKey = migratingFromPublicKey ?? peerPublicKey;
            var migrated = await ReassignAllHistoryAsync(sessionRepository, messageRepository, historySourceKey, newSession.Id, ct);
            if (migrated > 0)
                AppLog.Event("session.resync.history-migrated", ("peer", peerDisplayName), ("sessions", migrated), ("to", newSession.Id));

            // Identity migration (migratingFromPublicKey given): unlike an ordinary same-key resync,
            // THIS device already knows for certain the new identity is the same peer (the directory
            // match that got us here), so there's no need to wait for the other side to accept first
            // — resend right away instead of leaving it to whichever side accepts a fresh invite next
            // (see this method's own remarks on why an ordinary resync defers resending).
            if (migratingFromPublicKey is not null && migrated > 0)
                await ResendUndeliveredAsync(messagingService, messageRepository, messageTransport, newSession.Id, ct);
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
            m.Status is MessageStatus.Pending or MessageStatus.Sent or MessageStatus.Failed).ToList();

        await ResendAsync(messagingService, messageRepository, messageTransport, sessionId, undelivered, ct);
    }

    /// <summary>
    /// Background "send what is still stuck" sweep (2026-10-22, user: "apka musi vyresit sama"). A message
    /// whose live send did not happen — relay not connected at that moment, or the send threw — stays
    /// <see cref="MessageStatus.Pending"/> in storage, and until now NOTHING ever picked it up again except
    /// the accept-a-fresh-invite path (a code comment even promised "the connection supervisor will
    /// retry", which was never true): such a message showed 🕓 forever. Called by the supervisor on every
    /// reconnect and stale sweep. Only touches Pending/Failed rows older than <paramref name="minAge"/>
    /// (a message a chat page is sending right now must not be raced) in ACTIVE sessions — a session still
    /// mid-handshake delivers its backlog through the accept path, and a closed one through its resync.
    /// Messages already handed to the relay (Sent) are deliberately left alone: the relay holds them for
    /// an offline recipient.
    /// </summary>
    public static async Task RetryUnsentOutboundAsync(
        IMessagingService messagingService,
        IMessageRepository messageRepository,
        IChatSessionRepository sessionRepository,
        IMessageTransport messageTransport,
        TimeSpan minAge,
        CancellationToken ct = default)
    {
        if (!messageTransport.IsConnected) return;

        var unsent = await messageRepository.GetUnsentOutboundAsync(DateTimeOffset.UtcNow - minAge, ct);
        if (unsent.Count == 0) return;

        foreach (var bySession in unsent.GroupBy(m => m.ChatSessionId))
        {
            var session = await sessionRepository.GetByIdAsync(bySession.Key, ct);
            if (session is null || session.State != ChatSessionState.Active) continue;

            AppLog.Event("msg.retry-unsent", ("session", bySession.Key), ("count", bySession.Count()));
            await ResendAsync(messagingService, messageRepository, messageTransport, bySession.Key, bySession.ToList(), ct);
        }
    }

    /// <summary>One resend at a time process-wide: the accept path and the background sweep can both find the same stuck row, and each resend deletes the old row and mints a new one — two at once would send it twice.</summary>
    private static readonly SemaphoreSlim _resendGate = new(1, 1);

    private static async Task ResendAsync(
        IMessagingService messagingService,
        IMessageRepository messageRepository,
        IMessageTransport messageTransport,
        Guid sessionId,
        IReadOnlyList<Message> candidates,
        CancellationToken ct)
    {
        if (candidates.Count == 0) return;

        await _resendGate.WaitAsync(ct);
        try
        {
            await ResendLockedAsync(messagingService, messageRepository, messageTransport, sessionId, candidates, ct);
        }
        finally
        {
            _resendGate.Release();
        }
    }

    private static async Task ResendLockedAsync(
        IMessagingService messagingService,
        IMessageRepository messageRepository,
        IMessageTransport messageTransport,
        Guid sessionId,
        IReadOnlyList<Message> candidates,
        CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                // Re-read under the gate: a concurrent resend may already have replaced or delivered it.
                if (await messageRepository.GetByIdAsync(candidate.Id, ct) is not { } old
                    || old.Status is not (MessageStatus.Pending or MessageStatus.Sent or MessageStatus.Failed))
                    continue;

                var plaintext = await messagingService.DecryptMessageAsync(old.Id, ct);
                // Mint the replacement FIRST and drop the stale row only once that worked (2026-10-22):
                // the other way round, a failure while encrypting for the (possibly broken) session
                // deleted the user's message for good.
                var (fresh, envelope) = await messagingService.SendMessageAsync(
                    sessionId, plaintext,
                    attachmentDocumentId: old.AttachmentDocumentId,
                    attachmentLibraryFileId: old.AttachmentLibraryFileId,
                    attachmentFileName: old.AttachmentFileName,
                    groupChatId: old.GroupChatId,
                    groupMessageId: old.GroupMessageId,
                    ct: ct);
                await messageRepository.DeleteAsync(old.Id, ct);

                if (messageTransport.IsConnected)
                {
                    await messageTransport.SendEnvelopeAsync(envelope, ct);
                    // Without this the fresh row stayed Pending (🕓) forever although it HAD left the
                    // device — and the unsent-sweep would then have sent it again and again.
                    // Re-read first: the peer's ack can already have upgraded the row, and writing back
                    // a stale in-memory "Sent" would downgrade it.
                    if (await messageRepository.GetByIdAsync(fresh.Id, ct) is { Status: MessageStatus.Pending } current)
                    {
                        current.MarkSent();
                        await messageRepository.UpdateAsync(current, ct);
                    }
                }
                AppLog.Event("session.resend", ("session", sessionId), ("sent", messageTransport.IsConnected));
            }
            catch (Exception ex)
            {
                // Best-effort — one message failing to resend must not block the rest. A row that
                // could not be re-encrypted is still in place and is simply tried again next sweep.
                AppLog.Error("SessionRecovery.Resend", "failed to resend an undelivered message", ex);
            }
        }
    }
}
