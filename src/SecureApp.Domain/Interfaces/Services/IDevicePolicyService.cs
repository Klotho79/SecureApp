using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Fetches THIS device's admin-assigned policy from the relay (2026-09-24) — its role and which
/// tabs it must not show. Device-authenticated. Best-effort by contract: on any failure the caller
/// keeps whatever it already had, rather than a network blip wiping a member's access.
/// </summary>
public interface IDevicePolicyService
{
    Task<DevicePolicy> GetMyPolicyAsync(CancellationToken ct = default);
}
