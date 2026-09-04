namespace SecureApp.Domain.Enums;

/// <summary>Connection lifecycle state raised by <c>IMessageTransport.ConnectionStateChanged</c>.</summary>
public enum TransportConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}
