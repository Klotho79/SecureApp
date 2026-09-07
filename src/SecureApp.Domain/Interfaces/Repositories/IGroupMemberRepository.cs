using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IGroupMemberRepository
{
    Task<IReadOnlyList<GroupMember>> GetByGroupAsync(Guid groupChatId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the ENTIRE member list of a group in one call — a received group-invite carries a
    /// full membership snapshot rather than an incremental add/remove diff (see
    /// <c>IMessageTransport.SendGroupInviteAsync</c>'s remarks), so applying one is always "delete
    /// everything for this group, insert what the snapshot says" rather than reconciling deltas.
    /// Used for both the founder's own initial member list and every subsequent resync.
    /// </summary>
    Task ReplaceAllAsync(Guid groupChatId, IReadOnlyList<GroupMember> members, CancellationToken ct = default);

    Task DeleteByGroupAsync(Guid groupChatId, CancellationToken ct = default);
}
