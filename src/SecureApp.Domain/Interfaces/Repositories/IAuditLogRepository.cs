using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

/// <summary>Append-only by design: intentionally exposes no Update or Delete.</summary>
public interface IAuditLogRepository
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        Guid? documentId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default);
}
