using SecureApp.Domain.ValueObjects;

namespace SecureApp.Relay;

/// <summary>
/// The single JSON wire-frame shape used over the relay's WebSocket in both directions —
/// discriminated by <see cref="Type"/> ("auth", "authResult", "send", "deliver"), with unused
/// fields simply left null. Reuses the real <see cref="MessageEnvelope"/> Domain type directly
/// for <see cref="Envelope"/> rather than a hand-duplicated DTO, so the wire shape can never drift
/// from what <c>IMessagingService</c> actually produces/consumes.
/// </summary>
public sealed class RelayFrame
{
    public required string Type { get; init; }
    public Guid? DeviceId { get; init; }
    public string? Secret { get; init; }
    public bool? Success { get; init; }
    public Guid? RecipientDeviceId { get; init; }

    /// <summary>
    /// On a "deliver" frame, the device id of whoever actually sent this — attached by the relay
    /// itself from the authenticated sender connection, never trusted from client input. The
    /// recipient needs this to map an incoming message back to its own local chat session, since
    /// each side's <c>MessageEnvelope.SessionId</c> is a locally-generated id private to the
    /// sender and never matches the recipient's id for the "same" conversation.
    /// </summary>
    public Guid? SenderDeviceId { get; init; }

    public MessageEnvelope? Envelope { get; init; }

    /// <summary>
    /// On a "pairing"/"pairing-deliver" frame — an opaque, already-encrypted-in-substance blob the
    /// relay never parses (it's a client-encoded ChatInviteBlob, meaningless without the recipient's
    /// own crypto context). Routed exactly like Envelope on a "send"/"deliver" frame — see
    /// Program.cs's /ws handler — added alongside it (2026-09-06) rather than overloading Envelope
    /// itself, since a pairing invite has no SessionId to route by yet on the receiving side.
    /// </summary>
    public string? PairingInviteBlob { get; init; }

    /// <summary>On a "group-invite"/"group-invite-deliver" frame (2026-09-07) — a client-encoded GroupInviteBlob, opaque to the relay same as <see cref="PairingInviteBlob"/>. A separate field/frame type rather than reusing PairingInviteBlob, since the two carry different payload shapes and this codebase's own established call is to keep such things independently readable rather than generalized.</summary>
    public string? GroupInviteBlob { get; init; }
}
