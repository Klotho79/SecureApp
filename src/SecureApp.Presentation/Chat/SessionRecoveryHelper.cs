using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

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
            await messageTransport.SendPairingInviteAsync(peerRelayDeviceId, ContactCardCodec.Encode(invite), ct);
        // Not connected right now: the session stays PendingHandshake locally, same "no error
        // surfaced" policy this app already uses elsewhere. It isn't retried automatically the
        // instant the relay reconnects — App.AutoConnectRelayAsync's periodic health check (below)
        // is what eventually picks this back up.
    }
}
