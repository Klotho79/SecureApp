using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Data.Auditing;

/// <inheritdoc cref="IAuditLogger"/>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IAuditLogRepository _repository;

    public AuditLogger(IAuditLogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task LogAsync(AuditAction action, Guid? documentId = null, string? details = null, CancellationToken ct = default)
        => _repository.AppendAsync(new AuditLogEntry(action, documentId, details), ct);
}
