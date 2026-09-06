using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Transport;

/// <summary>
/// Client-side mirror of the relay's wire frame shape (<c>SecureApp.Relay.RelayFrame</c>) — kept in
/// sync by hand since the relay project and this MAUI app deliberately don't reference each other
/// (the relay must stay a plain ASP.NET Core service, not pull in MAUI/platform dependencies).
/// </summary>
internal sealed class WireFrame
{
    public required string Type { get; init; }
    public Guid? DeviceId { get; init; }
    public string? Secret { get; init; }
    public bool? Success { get; init; }
    public Guid? RecipientDeviceId { get; init; }

    /// <summary>
    /// On a "deliver" frame, the relay-attached device id of whoever actually sent this message —
    /// see <c>SecureApp.Relay.RelayFrame.SenderDeviceId</c>. Used by <see cref="WebSocketMessageTransport"/>
    /// to correlate an incoming envelope with the recipient's own local <c>ChatSession</c>.
    /// </summary>
    public Guid? SenderDeviceId { get; init; }

    public MessageEnvelope? Envelope { get; init; }

    /// <summary>
    /// On a "pairing" (client to relay) / "pairing-deliver" (relay to recipient) frame — the same
    /// <c>ContactCardCodec.Encode(ChatInviteBlob)</c> string a manual copy/paste or QR scan would
    /// carry, just delivered automatically over an already-open connection instead. See
    /// NewChatViewModel.CreateSessionAsync's own remarks (2026-09-06) for why: manual round-trip
    /// exchange is real, working, and stays as the fallback — this just removes the second manual
    /// step (initiator scans/pastes once, the reply travels back on its own) whenever both devices
    /// happen to be online, which is the common case, not the exception.
    /// </summary>
    public string? PairingInviteBlob { get; init; }
}
