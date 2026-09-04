namespace SecureApp.Presentation.Chat;

/// <summary>
/// What a device shares out-of-band (copy/paste today — no QR/camera pairing yet) before a peer
/// can start a chat with it. Not a Domain type — purely a Presentation-layer wire shape for
/// manual pairing, encoded/decoded via <see cref="ContactCardCodec"/>.
/// </summary>
public sealed record ContactCardBlob(string DisplayName, byte[] PublicKey, Guid RelayDeviceId);

/// <summary>What the initiator of a new chat sends back after <c>IMessagingService.CreateSessionAsync</c> so the peer can call <c>AcceptSessionAsync</c>.</summary>
public sealed record ChatInviteBlob(string InitiatorDisplayName, byte[] InitiatorPublicKey, Guid InitiatorRelayDeviceId, byte[] HandshakeCipherText);
