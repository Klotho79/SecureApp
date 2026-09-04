using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IUserRepository"/>
/// <remarks>
/// The <c>users</c> table only ever holds a single row (see <see cref="IUserRepository"/>'s
/// remarks), so <see cref="SaveCurrentUserAsync"/> replaces whatever row is there instead of
/// matching by id — works identically whether this is the very first save (no row yet) or an
/// update of the existing one.
/// </remarks>
public sealed class UserRepository : IUserRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public UserRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<User?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<UserRow>("SELECT * FROM users LIMIT 1");
        return rows.Count == 0 ? null : ToEntity(rows[0]);
    }

    public async Task SaveCurrentUserAsync(User user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM users");
        await connection.ExecuteAsync(
            "INSERT INTO users (id, display_name, role, created_at_utc, modified_at_utc) VALUES (?, ?, ?, ?, ?)",
            user.Id.ToString(),
            user.DisplayName,
            (int)user.Role,
            Format(user.CreatedAtUtc),
            Format(user.ModifiedAtUtc));
    }

    private static User ToEntity(UserRow row)
    {
        var entity = EntityMaterializer.Create<User>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(User.DisplayName), row.DisplayName);
        EntityMaterializer.Set(entity, nameof(User.Role), (Role)row.Role);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
