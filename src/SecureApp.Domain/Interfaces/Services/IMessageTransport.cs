using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Client-side abstraction over the (not-yet-built) message relay transport. Deliberately
/// WAN-capable — <see cref="ConnectAsync"/> takes a full <see cref="Uri"/>, not a bare LAN
/// host/port, so a future implementation can target a relay over VPN/DDNS/port-forward as easily
/// as a bare LAN address (see DEVELOPMENT_PLAN.md's Milestone 5 note). No implementation exists
/// yet in this pass — not registered in DI anywhere, same as <c>ISecureVaultKeyStore</c>/
/// <c>IDocumentRenderingService</c> before their Presentation-layer implementations existed.
///
/// Automatic + manual endpoint: future Presentation startup code reads
/// <c>ITransportSettingsRepository.GetAsync()</c> and, if
/// <c>TransportEndpointConfiguration.IsAutoConnectEnabled</c>, calls <see cref="ConnectAsync"/>
/// automatically; the user can change the endpoint manually at any time (future Settings UI →
/// <c>ITransportSettingsRepository.SaveAsync</c>), which the next automatic connect then picks up.
/// </summary>
public interface IMessageTransport
{
    bool IsConnected { get; }

    /// <summary>One-time provisioning: exchanges an invite code (issued out-of-band by the relay's admin) for this device's persistent relay identity + bearer secret.</summary>
    Task RegisterAsync(Uri endpoint, string inviteCode, string displayName, CancellationToken ct = default);

    Task ConnectAsync(Uri endpoint, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendEnvelopeAsync(MessageEnvelope envelope, CancellationToken ct = default);

    event EventHandler<MessageEnvelope>? EnvelopeReceived;
    event EventHandler<TransportConnectionState>? ConnectionStateChanged;
}
