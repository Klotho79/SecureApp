using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

/// <summary>Append-only by design once a message is sent/received: intentionally exposes no Delete, matching <c>IAuditLogRepository</c>'s precedent.</summary>
public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetBySessionAsync(Guid chatSessionId, DateTimeOffset? sinceUtc = null, CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
    Task UpdateAsync(Message message, CancellationToken ct = default);
}
