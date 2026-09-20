using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Syncs the shared company phone/extension directory across every device via the relay
/// (2026-09-20) — see <see cref="SharedContact"/>'s own remarks for the trust/encryption reasoning,
/// same as <c>ILogbookCatalogSyncService</c>'s established pattern (plain HTTP, device-authenticated,
/// not admin-gated server-side; RBAC on who gets to edit is enforced client-side, same as everywhere
/// else in this app).
/// </summary>
public interface ISharedContactService
{
    /// <summary>Every contact currently on the relay, ordered by SortOrder. Throws on a genuine failure — callers are expected to catch/swallow for their own best-effort framing.</summary>
    Task<IReadOnlyList<SharedContact>> FetchAsync(CancellationToken ct = default);

    /// <summary>Creates or updates one contact (matched by Id) — used for both a brand-new manual entry and a drag-reorder's SortOrder update. Best-effort: check the return value.</summary>
    Task<bool> PublishAsync(SharedContact contact, CancellationToken ct = default);

    /// <summary>Removes a contact from the shared directory for everyone. Best-effort: check the return value.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
