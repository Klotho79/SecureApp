namespace SecureApp.Domain.Enums;

/// <summary>Client-side view of one activation request's lifecycle — see <c>IMessageTransport.PollActivationAsync</c>'s remarks. Mirrors the relay's own "Pending"/"Approved"/"Rejected" strings (<c>SecureApp.Relay.Contracts.ActivationStatusResponse</c>) as a typed enum on this side of the wire.</summary>
public enum ActivationRequestStatus
{
    Pending,
    Approved,
    Rejected
}
