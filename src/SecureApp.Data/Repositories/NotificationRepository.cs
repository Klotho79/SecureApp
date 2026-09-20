using System.Globalization;
using System.Text;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="INotificationRepository"/>
public sealed class NotificationRepository : INotificationRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public NotificationRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<NotificationRow>("SELECT * FROM notifications WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<Notification>> GetPagedAsync(NotificationFilter filter, int take, DateTimeOffset? createdBeforeUtc = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var sql = new StringBuilder("SELECT * FROM notifications WHERE 1 = 1");
        var args = new List<object?>();

        if (filter.ArchivedOnly)
            sql.Append(" AND is_archived = 1");
        else if (!filter.IncludeArchived)
            sql.Append(" AND is_archived = 0");

        if (filter.OnlyImportant)
            sql.Append(" AND (priority >= ? OR is_manually_important = 1)").AsIs(args, (int)NotificationPriority.Important);

        if (filter.OnlyUnread)
            sql.Append(" AND is_read = 0");

        if (filter.Category is { } category)
            sql.Append(" AND category = ?").AsIs(args, (int)category);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            sql.Append(" AND (title LIKE ? OR body LIKE ?)").AsIs(args, $"%{filter.SearchText}%", $"%{filter.SearchText}%");

        if (createdBeforeUtc is { } before)
            sql.Append(" AND created_at_utc < ?").AsIs(args, Format(before));

        sql.Append(" ORDER BY created_at_utc DESC LIMIT ?");
        args.Add(take);

        var rows = await connection.QueryAsync<NotificationRow>(sql.ToString(), args.ToArray());
        return rows.Select(ToEntity).ToList();
    }

    public async Task<NotificationCounts> GetCountsAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var important = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE is_archived = 0 AND (priority >= ? OR is_manually_important = 1)",
            (int)NotificationPriority.Important);
        var unread = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE is_archived = 0 AND is_read = 0");

        // "Today" in the device's own local calendar day, not a UTC calendar day — a notification
        // from 23:30 local time must count as today even though its UTC timestamp already rolled
        // into tomorrow. created_at_utc is stored ISO-8601 with a UTC offset, so comparing against
        // the local day boundary (itself converted to UTC) is still a plain, index-usable string
        // comparison — same trick the rest of this schema relies on for date filtering.
        var todayStartUtc = new DateTimeOffset(DateTime.Today).ToUniversalTime();
        var today = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM notifications WHERE is_archived = 0 AND created_at_utc >= ?",
            Format(todayStartUtc));

        return new NotificationCounts(important, unread, today);
    }

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO notifications (
                id, title, body, category, priority, is_read, is_archived, is_manually_important,
                related_chat_session_id, related_group_chat_id, related_library_file_id,
                created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            notification.Id.ToString(),
            notification.Title,
            notification.Body,
            (int)notification.Category,
            (int)notification.Priority,
            notification.IsRead,
            notification.IsArchived,
            notification.IsManuallyImportant,
            notification.RelatedChatSessionId?.ToString(),
            notification.RelatedGroupChatId?.ToString(),
            notification.RelatedLibraryFileId?.ToString(),
            Format(notification.CreatedAtUtc),
            Format(notification.ModifiedAtUtc));
    }

    public async Task UpdateAsync(Notification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            UPDATE notifications
            SET is_read = ?, is_archived = ?, is_manually_important = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            notification.IsRead,
            notification.IsArchived,
            notification.IsManuallyImportant,
            Format(notification.ModifiedAtUtc),
            notification.Id.ToString());
    }

    private static Notification ToEntity(NotificationRow row)
    {
        var entity = EntityMaterializer.Create<Notification>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(Notification.Title), row.Title);
        EntityMaterializer.Set(entity, nameof(Notification.Body), row.Body);
        EntityMaterializer.Set(entity, nameof(Notification.Category), (NotificationCategory)row.Category);
        EntityMaterializer.Set(entity, nameof(Notification.Priority), (NotificationPriority)row.Priority);
        EntityMaterializer.Set(entity, nameof(Notification.IsRead), row.IsRead != 0);
        EntityMaterializer.Set(entity, nameof(Notification.IsArchived), row.IsArchived != 0);
        EntityMaterializer.Set(entity, nameof(Notification.IsManuallyImportant), row.IsManuallyImportant != 0);
        EntityMaterializer.Set(entity, nameof(Notification.RelatedChatSessionId), row.RelatedChatSessionId is null ? null : (Guid?)Guid.Parse(row.RelatedChatSessionId));
        EntityMaterializer.Set(entity, nameof(Notification.RelatedGroupChatId), row.RelatedGroupChatId is null ? null : (Guid?)Guid.Parse(row.RelatedGroupChatId));
        EntityMaterializer.Set(entity, nameof(Notification.RelatedLibraryFileId), row.RelatedLibraryFileId is null ? null : (Guid?)Guid.Parse(row.RelatedLibraryFileId));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}

/// <summary>Tiny fluent helper so <see cref="NotificationRepository.GetPagedAsync"/>'s conditional-clause building stays one line each instead of a separate <c>args.Add(...)</c> statement per branch.</summary>
file static class StringBuilderExtensions
{
    public static StringBuilder AsIs(this StringBuilder sb, List<object?> args, params object?[] values)
    {
        args.AddRange(values);
        return sb;
    }
}
