using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// Append-only security audit trail entry. Audit entries are never mutated or
/// deleted by application code — only ever inserted via <c>IAuditLogRepository</c>.
/// </summary>
public sealed class AuditLogEntry : Entity
{
    public AuditAction Action { get; private set; }
    public Guid? DocumentId { get; private set; }
    public string? Details { get; private set; }

    private AuditLogEntry() { }

    public AuditLogEntry(AuditAction action, Guid? documentId = null, string? details = null)
    {
        Action = action;
        DocumentId = documentId;
        Details = details;
    }
}
