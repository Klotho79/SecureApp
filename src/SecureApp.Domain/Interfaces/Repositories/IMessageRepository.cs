using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

/// <summary>
/// Was append-only by design; message deletion (2026-09-11, the user's own request — see
/// <c>RoleAccessPolicy.CanDeleteMessage</c> for the RBAC rules) adds the two Delete methods below.
/// The audit trail of who-deleted-what is not itself removed — <c>IAuditLogRepository</c> keeps its
/// append-only precedent; only the redecryptable message body is removed.
/// </summary>
public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetBySessionAsync(Guid chatSessionId, DateTimeOffset? sinceUtc = null, CancellationToken ct = default);

    /// <summary>Every message row tagged with this group (2026-09-07) — spans every member's own pairwise <c>ChatSession</c>, not just one, since a group message's fan-out legs each live under a different session id. The caller (a group's own thread view) is responsible for collapsing the sender's own N outbound copies of one logical message back down via <see cref="Message.GroupMessageId"/> — see <c>GroupChat</c>'s own remarks.</summary>
    Task<IReadOnlyList<Message>> GetByGroupAsync(Guid groupChatId, CancellationToken ct = default);

    Task AddAsync(Message message, CancellationToken ct = default);
    Task UpdateAsync(Message message, CancellationToken ct = default);

    /// <summary>Deletes a single local message row by its per-device <see cref="Message.Id"/> — used for a local-only delete of a message that has no cross-device correlation id (one sent before that was tracked). Idempotent: deleting a row that's already gone is a no-op.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Deletes every local row that is a copy of the SAME logical message as identified by a
    /// cross-device correlation id — matches rows whose <see cref="Message.OriginMessageId"/> OR
    /// <see cref="Message.GroupMessageId"/> equals <paramref name="correlationId"/>. One call removes
    /// both a 1:1 message and (on the sender's side) all N fan-out legs of a group message. Idempotent.
    /// </summary>
    Task DeleteByCorrelationAsync(Guid correlationId, CancellationToken ct = default);
}
