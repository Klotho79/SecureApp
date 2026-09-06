namespace SecureApp.Relay;

public sealed record CreateInviteRequest(string? DisplayNameHint, int ValidForMinutes = 60);
public sealed record CreateInviteResponse(string Code, DateTimeOffset ExpiresAtUtc);

public sealed record CreateDeviceRequest(string DisplayName);
public sealed record DeviceCredentialResponse(Guid DeviceId, string Secret);

public sealed record RegisterRequest(string InviteCode, string DisplayName);

// --- Activation requests (2026-09-06) — replaces the invite-code hand-off above for a new device
// joining the community. Instead of an admin minting a code that then has to travel out-of-band
// (SMS/WhatsApp/in person) to the new device, the new device sends the admin a request directly
// (name + email + a short fingerprint of its own identity key) and the admin approves it in-app —
// see IRelayAdminService/IMessageTransport's own remarks for the full flow. The old invite-code
// endpoints above are left in place, unused by the app now, rather than removed — no harm in a
// server-side path nothing calls, and it stays available as a manual curl fallback per
// relay/ops/README.md if this flow is ever unreachable.

public sealed record CreateActivationRequestRequest(string DisplayName, string Email, string KeyFingerprint);
public sealed record CreateActivationRequestResponse(Guid RequestId);

/// <summary>Polled by the still-unregistered device. Once <c>Status</c> is "Approved", <c>DeviceId</c>/<c>Secret</c> are populated — the same credential a successful <c>/register</c> call would have returned, just delivered asynchronously once a human approves rather than synchronously against a pre-shared code.</summary>
public sealed record ActivationStatusResponse(string Status, Guid? DeviceId, string? Secret);

/// <summary>One row in the admin's pending-activations list — deliberately carries no secret, only what the admin needs to decide whether to approve.</summary>
public sealed record ActivationRequestSummary(Guid Id, string DisplayName, string Email, string KeyFingerprint, DateTimeOffset CreatedAtUtc);
