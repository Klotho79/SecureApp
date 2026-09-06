using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Lets a device whose local <see cref="SecureApp.Domain.Enums.Role"/> is Admin mint relay invite
/// codes in-app, instead of the relay operator having to SSH into the Pi and run curl against
/// <c>POST /admin/invites</c> by hand. The relay's admin secret (<c>SECUREAPP_RELAY_ADMIN_SECRET</c>)
/// is typed in once and held in this device's vault — see <see cref="SetAdminSecretAsync"/> — never
/// transmitted anywhere except as the <c>X-Admin-Secret</c> header on this call.
/// </summary>
public interface IRelayAdminService
{
    Task<bool> HasAdminSecretAsync(CancellationToken ct = default);

    Task SetAdminSecretAsync(string adminSecret, CancellationToken ct = default);

    /// <summary>Mints a fresh invite code good for <paramref name="validForMinutes"/> minutes. Throws if no admin secret is stored yet, or the relay rejects the stored one. Superseded in the app's own UI by the activation-request review below (2026-09-06) — kept as a working, just-unused-by-the-app path, not removed.</summary>
    Task<(string InviteCode, DateTimeOffset ExpiresAtUtc)> CreateInviteAsync(Uri endpoint, string? displayNameHint, int validForMinutes, CancellationToken ct = default);

    /// <summary>Every activation request still awaiting a decision — see <see cref="PendingActivationRequest"/>'s own remarks.</summary>
    Task<IReadOnlyList<PendingActivationRequest>> GetPendingActivationRequestsAsync(Uri endpoint, CancellationToken ct = default);

    /// <summary>Approves one pending request — mints it a device credential on the relay, which the requesting device then picks up on its next <c>IMessageTransport.PollActivationAsync</c> call. Throws (surfaced as an error, not silently ignored) if the request was already decided by the time this runs — e.g. a double-tap, or another admin device got there first.</summary>
    Task ApproveActivationRequestAsync(Uri endpoint, Guid requestId, CancellationToken ct = default);

    /// <summary>Rejects one pending request — same "already decided" error behavior as <see cref="ApproveActivationRequestAsync"/>.</summary>
    Task RejectActivationRequestAsync(Uri endpoint, Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Asks the relay to rebuild and restart itself from whatever code a prior <c>git push</c>
    /// already checked out on the Pi (this call carries no code, just the request) — see
    /// <c>relay/ops/README.md</c> for the full pipeline. Returns once the relay has queued the
    /// request; the actual rebuild happens out-of-band on the Pi's host, not synchronously here.
    /// </summary>
    Task RequestDeployAsync(Uri endpoint, CancellationToken ct = default);
}
