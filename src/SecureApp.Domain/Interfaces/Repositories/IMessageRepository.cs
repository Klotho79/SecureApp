using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;

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

    /// <summary>A cheap change-signature for a 1:1 session's visible messages (count + newest timestamp) — lets the UI keep a warm in-memory copy of a thread and only rebuild it when this actually changes, without reading/decrypting every row (2026-09-11).</summary>
    Task<string> GetSessionSignatureAsync(Guid chatSessionId, CancellationToken ct = default);

    /// <summary>The group counterpart of <see cref="GetSessionSignatureAsync"/>.</summary>
    Task<string> GetGroupSignatureAsync(Guid groupChatId, CancellationToken ct = default);

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

    /// <summary>
    /// Marks this device's own outbound message Delivered when the recipient's app acknowledged receipt
    /// (2026-09-14, delivery receipts / WhatsApp-style ✓✓). Matches by cross-device correlation id
    /// (<see cref="Message.OriginMessageId"/> or a group message's <see cref="Message.GroupMessageId"/>),
    /// only upgrades a still-Pending/Sent row, and is idempotent. Returns true if a row was upgraded.
    /// </summary>
    Task<bool> MarkDeliveredByCorrelationAsync(Guid correlationId, CancellationToken ct = default);

    /// <summary>Marks the one outbound fan-out leg on <paramref name="chatSessionId"/> Delivered (2026-09-14, group delivery receipts) — an ack arrives on the acking member's session, so this upgrades only that member's leg. Also correct for a 1:1 (single leg). Idempotent; returns true if a row was upgraded.</summary>
    Task<bool> MarkDeliveredForSessionAsync(Guid chatSessionId, Guid correlationId, CancellationToken ct = default);

    /// <summary>The aggregate delivery status of an outbound message — the MINIMUM status across all its fan-out legs, so a group message reads Delivered only once EVERY member's leg is Delivered. Null if no such outbound row exists.</summary>
    Task<MessageStatus?> GetOutboundAggregateStatusAsync(Guid correlationId, CancellationToken ct = default);

    /// <summary>
    /// Re-parents every message row from one session to another (2026-09-15) — a session that gets
    /// closed and replaced by a resync (see <c>SessionRecoveryHelper</c> in the Presentation layer)
    /// otherwise strands its whole prior history: the thread view only ever queries the CURRENT
    /// session id, so once the peer starts using the new session, every message still filed under the
    /// old, now-Closed one silently stops showing up — nothing is deleted, it just becomes invisible.
    /// A no-op if <paramref name="oldChatSessionId"/> has no rows.
    /// </summary>
    Task ReassignSessionAsync(Guid oldChatSessionId, Guid newChatSessionId, CancellationToken ct = default);
}
