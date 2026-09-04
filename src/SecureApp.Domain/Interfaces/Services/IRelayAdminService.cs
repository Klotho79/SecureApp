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

    /// <summary>Mints a fresh invite code good for <paramref name="validForMinutes"/> minutes. Throws if no admin secret is stored yet, or the relay rejects the stored one.</summary>
    Task<(string InviteCode, DateTimeOffset ExpiresAtUtc)> CreateInviteAsync(Uri endpoint, string? displayNameHint, int validForMinutes, CancellationToken ct = default);

    /// <summary>
    /// Asks the relay to rebuild and restart itself from whatever code a prior <c>git push</c>
    /// already checked out on the Pi (this call carries no code, just the request) — see
    /// <c>relay/ops/README.md</c> for the full pipeline. Returns once the relay has queued the
    /// request; the actual rebuild happens out-of-band on the Pi's host, not synchronously here.
    /// </summary>
    Task RequestDeployAsync(Uri endpoint, CancellationToken ct = default);
}
