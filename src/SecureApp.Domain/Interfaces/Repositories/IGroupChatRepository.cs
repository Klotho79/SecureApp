using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IGroupChatRepository
{
    Task<GroupChat?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GroupChat>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Insert-or-replace by <see cref="GroupChat.Id"/> — a received group-invite snapshot (see <c>GroupChat</c>'s own remarks on shared ids) is applied the same way whether this is a brand new group or a rename/membership resync of one already known locally.</summary>
    Task UpsertAsync(GroupChat groupChat, CancellationToken ct = default);

    /// <summary>Leaving/deleting a group locally — does not notify other members (see <c>DEVELOPMENT_PLAN.md</c>'s remarks on this being a deliberately local-only operation for this pass).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
