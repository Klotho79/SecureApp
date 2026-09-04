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
}
