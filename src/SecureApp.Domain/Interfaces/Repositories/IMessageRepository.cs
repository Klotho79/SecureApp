using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

/// <summary>Append-only by design once a message is sent/received: intentionally exposes no Delete, matching <c>IAuditLogRepository</c>'s precedent.</summary>
public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetBySessionAsync(Guid chatSessionId, DateTimeOffset? sinceUtc = null, CancellationToken ct = default);

    /// <summary>Every message row tagged with this group (2026-09-07) — spans every member's own pairwise <c>ChatSession</c>, not just one, since a group message's fan-out legs each live under a different session id. The caller (a group's own thread view) is responsible for collapsing the sender's own N outbound copies of one logical message back down via <see cref="Message.GroupMessageId"/> — see <c>GroupChat</c>'s own remarks.</summary>
    Task<IReadOnlyList<Message>> GetByGroupAsync(Guid groupChatId, CancellationToken ct = default);

    Task AddAsync(Message message, CancellationToken ct = default);
    Task UpdateAsync(Message message, CancellationToken ct = default);
}
