namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One row in the Admin's "pending activations" list (2026-09-06) — see
/// <c>IRelayAdminService.GetPendingActivationRequestsAsync</c>'s remarks. <see cref="KeyFingerprint"/>
/// is a short, human-readable digest of the requesting device's own chat-identity public key (the
/// same key <c>IMessagingService.GetLocalIdentityPublicKeyAsync</c> already exposes for contact
/// cards) — not a secret, just something the admin can optionally read back to the person out of
/// band (phone call, in person) for extra assurance before approving.
/// </summary>
public sealed record PendingActivationRequest(Guid Id, string DisplayName, string Email, string KeyFingerprint, DateTimeOffset CreatedAtUtc);
