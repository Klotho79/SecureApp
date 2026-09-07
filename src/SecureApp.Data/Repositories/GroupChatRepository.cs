using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IGroupChatRepository"/>
public sealed class GroupChatRepository : IGroupChatRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public GroupChatRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<GroupChat?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<GroupChatRow>("SELECT * FROM group_chats WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<GroupChat>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<GroupChatRow>("SELECT * FROM group_chats ORDER BY modified_at_utc DESC");
        return rows.Select(ToEntity).ToList();
    }

    public async Task UpsertAsync(GroupChat groupChat, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(groupChat);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO group_chats (id, name, founder_public_key, created_at_utc, modified_at_utc)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, founder_public_key = excluded.founder_public_key, modified_at_utc = excluded.modified_at_utc
            """,
            groupChat.Id.ToString(),
            groupChat.Name,
            groupChat.FounderPublicKey,
            Format(groupChat.CreatedAtUtc),
            Format(groupChat.ModifiedAtUtc));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM group_chats WHERE id = ?", id.ToString());
    }

    private static GroupChat ToEntity(GroupChatRow row)
    {
        var entity = EntityMaterializer.Create<GroupChat>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(GroupChat.Name), row.Name);
        EntityMaterializer.Set(entity, nameof(GroupChat.FounderPublicKey), row.FounderPublicKey);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
