using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>Convenience façade over <c>IAuditLogRepository</c> for recording security-relevant events.</summary>
public interface IAuditLogger
{
    Task LogAsync(AuditAction action, Guid? documentId = null, string? details = null, CancellationToken ct = default);
}
