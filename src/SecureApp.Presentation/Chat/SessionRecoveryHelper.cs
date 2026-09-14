using System.Collections.Concurrent;
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

        var (_, handshakeCipherText) = await messagingService.CreateSessionAsync(peerDisplayName, peerPublicKey, peerRelayDeviceId, ct);
        var invite = new ChatInviteBlob(currentUserService.Current.DisplayName, ownPublicKey, ownDeviceId, handshakeCipherText);

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
}
