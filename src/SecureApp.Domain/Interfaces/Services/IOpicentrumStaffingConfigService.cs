using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Relay-backed store of the position-suggestion staffing config (2026-10-25, POZICE_NAVRH_PLAN.md) — the same
/// singleton-blob contract as <see cref="IOpicentrumRosterBoardService"/>, but writable by Admin only.
/// </summary>
public interface IOpicentrumStaffingConfigService
{
    Task<OpicentrumStaffingConfig?> GetCachedAsync(CancellationToken ct = default);

    Task PublishAsync(OpicentrumStaffingConfig config, CancellationToken ct = default);
}
