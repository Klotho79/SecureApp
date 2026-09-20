using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Syncs the shared company workplace list across every device via the relay (2026-09-20, Phase 5) —
/// see <see cref="Workplace"/>'s own remarks for the trust/encryption reasoning, same established
/// pattern as <see cref="ISharedContactService"/>.
/// </summary>
public interface IWorkplaceCatalogService
{
    /// <summary>Every workplace currently on the relay, ordered by name. Throws on a genuine failure — callers are expected to catch/swallow for their own best-effort framing.</summary>
    Task<IReadOnlyList<Workplace>> FetchAsync(CancellationToken ct = default);

    /// <summary>Creates or updates one workplace (matched by Id). Best-effort: check the return value.</summary>
    Task<bool> PublishAsync(Workplace workplace, CancellationToken ct = default);

    /// <summary>Removes a workplace from the shared catalog for everyone. Best-effort: check the return value.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
