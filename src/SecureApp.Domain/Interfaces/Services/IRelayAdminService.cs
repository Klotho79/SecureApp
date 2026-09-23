using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Lets a device whose local <see cref="SecureApp.Domain.Enums.Role"/> is Admin mint relay invite
/// codes, manage registered devices, and request a redeploy in-app, instead of the relay operator
/// having to SSH into the Pi and run curl by hand. The relay's admin secret
/// (<c>SECUREAPP_RELAY_ADMIN_SECRET</c>) is deliberately NEVER persisted anywhere on this device
/// (2026-09-07, the user's own explicit call after reviewing the original persisted-secret design:
/// even OS-backed secure storage only protects against someone without this device's own login —
/// not against anything already running under it) — every method here takes it as a plain
/// parameter, supplied fresh by the caller (<c>SettingsViewModel</c>'s <c>AdminSecretInputText</c>,
/// re-typed before each admin action and cleared right after) for that one call only.
/// </summary>
public interface IRelayAdminService
{
    /// <summary>Mints a fresh invite code good for <paramref name="validForMinutes"/> minutes.</summary>
    Task<(string InviteCode, DateTimeOffset ExpiresAtUtc)> CreateInviteAsync(Uri endpoint, string adminSecret, string? displayNameHint, int validForMinutes, CancellationToken ct = default);

    /// <summary>
    /// Asks the relay to rebuild and restart itself from whatever code a prior <c>git push</c>
    /// already checked out on the Pi (this call carries no code, just the request) — see
    /// <c>relay/ops/README.md</c> for the full pipeline. Returns once the relay has queued the
    /// request; the actual rebuild happens out-of-band on the Pi's host, not synchronously here.
    /// </summary>
    Task RequestDeployAsync(Uri endpoint, string adminSecret, CancellationToken ct = default);

    /// <summary>Every registered device with its directory staleness + pending-outbox depth (2.1, 2026-09-17) — see <see cref="ValueObjects.RegisteredDevice"/>'s own remarks. Lets an admin actually see a ghost identity forming instead of only discovering it mid-incident.</summary>
    Task<IReadOnlyList<RegisteredDevice>> GetRegisteredDevicesAsync(Uri endpoint, string adminSecret, CancellationToken ct = default);

    /// <summary>Deregisters a device entirely — deletes its credential, its directory entry, and purges its stuck outbox queue (see <c>RelayDatabase.DeregisterDevice</c>'s own remarks). Idempotent: deregistering an already-gone device is not an error.</summary>
    Task DeregisterDeviceAsync(Uri endpoint, string adminSecret, Guid deviceId, CancellationToken ct = default);

    /// <summary>
    /// New-member onboarding (2026-09-23, user's own ask: "apka sama... předala kódy" — a brand-new
    /// member has no network access at all, so someone already inside needs to mint them a WireGuard
    /// peer before anything else the app does is reachable). Creates a fresh wg-easy client named
    /// <paramref name="memberName"/> and returns its raw WireGuard <c>.conf</c> text — the caller
    /// renders its own QR from it (same <c>QrImageGenerator</c> already used for the SecureApp-
    /// download QR), never round-tripping wg-easy's own SVG QR format. The relay's own wg-easy
    /// credential never reaches this device at all — only the resulting config text does.
    /// </summary>
    Task<string> CreateWireGuardClientAsync(Uri endpoint, string adminSecret, string memberName, CancellationToken ct = default);
}
