using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

// See RequestActivationAsync/PollActivationAsync's own remarks below for the 2026-09-06 activation
// flow this file adds — it deliberately sits on IMessageTransport rather than a new interface, the
// same way RegisterAsync (the flow it replaces) already did: both are one-time provisioning steps
// for this device's relay identity, not ongoing messaging.

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

    /// <summary>One-time provisioning: exchanges an invite code (issued out-of-band by the relay's admin) for this device's persistent relay identity + bearer secret. Superseded in the app's own UI by <see cref="RequestActivationAsync"/>/<see cref="PollActivationAsync"/> below (2026-09-06) — kept here (and on the relay) as a working, just-unused-by-the-app path, not removed.</summary>
    Task RegisterAsync(Uri endpoint, string inviteCode, string displayName, CancellationToken ct = default);

    /// <summary>
    /// Sends the admin a request to activate this device — name + email (both just for the admin to
    /// recognize who's asking; not otherwise used by the protocol) plus a short fingerprint of this
    /// device's own chat-identity public key (see <see cref="PendingActivationRequest.KeyFingerprint"/>'s
    /// remarks). Returns the request id to pass into <see cref="PollActivationAsync"/>; the caller is
    /// responsible for persisting it (<c>TransportEndpointConfiguration.SetPendingActivationRequest</c>)
    /// so a relaunch before the admin responds can resume polling instead of losing track of it.
    /// </summary>
    Task<Guid> RequestActivationAsync(Uri endpoint, string displayName, string email, CancellationToken ct = default);

    /// <summary>
    /// Checks one activation request's status. On <see cref="ActivationRequestStatus.Approved"/>,
    /// this completes registration transparently (stores the device secret in the vault, assigns
    /// the device id — the same tail <see cref="RegisterAsync"/> already runs) before returning, so
    /// the caller just needs to keep polling until the result isn't <see cref="ActivationRequestStatus.Pending"/>
    /// and then stop. Safe to call repeatedly after approval (idempotent — see the relay's own
    /// remarks on <c>/activation/status</c>).
    /// </summary>
    Task<ActivationRequestStatus> PollActivationAsync(Uri endpoint, Guid requestId, CancellationToken ct = default);

    Task ConnectAsync(Uri endpoint, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendEnvelopeAsync(MessageEnvelope envelope, CancellationToken ct = default);

    /// <summary>
    /// Delivers a pairing invite (the same blob a manual copy/paste or QR scan would otherwise
    /// carry — see <c>ContactCardCodec.Encode(ChatInviteBlob)</c>) directly over this already-open
    /// connection, so a peer who's online never needs the manual round-trip at all. Best-effort by
    /// design (throws if not connected, same as <see cref="SendEnvelopeAsync"/>) — the manual
    /// QR/copy-paste path this replaces for the common case stays fully functional as the fallback.
    /// </summary>
    Task SendPairingInviteAsync(Guid recipientRelayDeviceId, string inviteBlob, CancellationToken ct = default);

    event EventHandler<MessageEnvelope>? EnvelopeReceived;

    /// <summary>Raised when a pairing invite arrives via <see cref="SendPairingInviteAsync"/> from the other side — the raw blob, ready for <c>ContactCardCodec.Decode&lt;ChatInviteBlob&gt;</c> exactly like a manually pasted/scanned one.</summary>
    event EventHandler<string>? PairingInviteReceived;

    event EventHandler<TransportConnectionState>? ConnectionStateChanged;
}
