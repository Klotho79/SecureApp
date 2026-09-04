using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>
/// The device's stored default relay endpoint for <c>IMessageTransport</c>. Exactly one instance
/// is ever persisted (see <c>ITransportSettingsRepository</c>'s remarks) — mirrors <see cref="User"/>'s
/// single-row model. Lets the app auto-connect on startup (<see cref="IsAutoConnectEnabled"/>) while
/// still allowing the endpoint to be changed manually at any time.
/// </summary>
public sealed class TransportEndpointConfiguration : Entity
{
    public Uri? EndpointUri { get; private set; }
    public bool IsAutoConnectEnabled { get; private set; }
    public DateTimeOffset? LastConnectedAtUtc { get; private set; }

    /// <summary>This device's own relay-assigned routing identity, obtained via <c>IMessageTransport.RegisterAsync</c>. Not a cryptographic identity — see <c>ChatSession.PeerRelayDeviceId</c>'s remarks for the same distinction on the peer side. The actual bearer secret is never stored here — it lives only in the vault.</summary>
    public Guid? AssignedDeviceId { get; private set; }

    private TransportEndpointConfiguration() { }

    public TransportEndpointConfiguration(Uri? endpointUri, bool isAutoConnectEnabled)
    {
        EndpointUri = endpointUri;
        IsAutoConnectEnabled = isAutoConnectEnabled;
    }

    public void SetEndpoint(Uri? endpointUri, bool isAutoConnectEnabled)
    {
        EndpointUri = endpointUri;
        IsAutoConnectEnabled = isAutoConnectEnabled;
        Touch();
    }

    public void NoteConnected()
    {
        LastConnectedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void AssignDevice(Guid deviceId)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device id cannot be empty.", nameof(deviceId));

        AssignedDeviceId = deviceId;
        Touch();
    }
}
