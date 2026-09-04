using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

/// <summary>One row per <c>ChatSession</c> (same id) — see <see cref="RatchetSessionState"/>'s remarks.</summary>
public interface IRatchetSessionStateRepository
{
    Task<RatchetSessionState?> GetBySessionIdAsync(Guid chatSessionId, CancellationToken ct = default);
    Task UpsertAsync(RatchetSessionState state, CancellationToken ct = default);
}
