namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// The single JSON frame shape used over the relay's WebSocket in both directions, shared by the app and the relay
/// (2026-11-14: it used to be two hand-synced copies, the client's <c>WireFrame</c> and the relay's <c>RelayFrame</c>).
/// Discriminated by <see cref="Type"/> ("auth", "authResult", "send", "deliver", "pairing", "pairing-deliver",
/// "group-invite", "group-invite-deliver", "board-changed"); unused fields stay null. <see cref="Envelope"/> is the real
/// <see cref="MessageEnvelope"/> so the wire shape cannot drift from what the messaging service produces.
/// </summary>
public sealed class WireFrame
{
    /// <summary>Client-side stand-in for a frame that arrived but could not be parsed (malformed JSON, empty message). The receive loop skips it instead of treating it as "connection closed". Never sent.</summary>
    public static readonly WireFrame Unreadable = new() { Type = "unreadable" };

    public required string Type { get; init; }
    public Guid? DeviceId { get; init; }
    public string? Secret { get; init; }
    public bool? Success { get; init; }
    public Guid? RecipientDeviceId { get; init; }

    /// <summary>
    /// On a "deliver" frame, the device id of whoever sent the message. The relay attaches it from the authenticated
    /// sender connection and never trusts client input for it. The recipient needs it to map the message to its own
    /// chat session, because each side's <c>MessageEnvelope.SessionId</c> is a locally generated id.
    /// </summary>
    public Guid? SenderDeviceId { get; init; }

    public MessageEnvelope? Envelope { get; init; }

    /// <summary>On a "pairing"/"pairing-deliver" frame: a client-encoded ChatInviteBlob, opaque to the relay and routed like <see cref="Envelope"/>. A separate field because a pairing invite has no session to route by yet.</summary>
    public string? PairingInviteBlob { get; init; }

    /// <summary>On a "group-invite"/"group-invite-deliver" frame: a client-encoded GroupInviteBlob, opaque to the relay like <see cref="PairingInviteBlob"/>.</summary>
    public string? GroupInviteBlob { get; init; }

    /// <summary>On a "board-changed" frame (relay to client only): which Opicentrum board was replaced, "roster" or "volno". No payload; the client re-fetches it over HTTP.</summary>
    public string? Board { get; init; }
}
