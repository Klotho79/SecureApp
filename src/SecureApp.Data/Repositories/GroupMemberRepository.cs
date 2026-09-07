using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IGroupMemberRepository"/>
/// <remarks><see cref="ReplaceAllAsync"/> is "delete then insert" — same unwrapped-by-a-transaction shape <c>TransportSettingsRepository.SaveAsync</c> already uses for its own single-row replace, not new to this codebase.</remarks>
public sealed class GroupMemberRepository : IGroupMemberRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public GroupMemberRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<GroupMember>> GetByGroupAsync(Guid groupChatId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<GroupMemberRow>("SELECT * FROM group_members WHERE group_chat_id = ? ORDER BY display_name COLLATE NOCASE", groupChatId.ToString());
        return rows.Select(ToEntity).ToList();
    }

    public async Task ReplaceAllAsync(Guid groupChatId, IReadOnlyList<GroupMember> members, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(members);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM group_members WHERE group_chat_id = ?", groupChatId.ToString());

        foreach (var member in members)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO group_members (id, group_chat_id, display_name, public_key, relay_device_id, can_invite, created_at_utc, modified_at_utc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                member.Id.ToString(),
                groupChatId.ToString(),
                member.DisplayName,
                member.PublicKey,
                member.RelayDeviceId.ToString(),
                member.CanInvite ? 1 : 0,
                Format(member.CreatedAtUtc),
                Format(member.ModifiedAtUtc));
        }
    }

    public async Task DeleteByGroupAsync(Guid groupChatId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM group_members WHERE group_chat_id = ?", groupChatId.ToString());
    }

    private static GroupMember ToEntity(GroupMemberRow row)
    {
        var entity = EntityMaterializer.Create<GroupMember>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(GroupMember.GroupChatId), Guid.Parse(row.GroupChatId));
        EntityMaterializer.Set(entity, nameof(GroupMember.DisplayName), row.DisplayName);
        EntityMaterializer.Set(entity, nameof(GroupMember.PublicKey), row.PublicKey);
        EntityMaterializer.Set(entity, nameof(GroupMember.RelayDeviceId), Guid.Parse(row.RelayDeviceId));
        EntityMaterializer.Set(entity, nameof(GroupMember.CanInvite), row.CanInvite != 0);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
