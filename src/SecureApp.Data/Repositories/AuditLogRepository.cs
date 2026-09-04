using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IAuditLogRepository"/>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public AuditLogRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            "INSERT INTO audit_log_entries (id, action, document_id, details, created_at_utc, modified_at_utc) VALUES (?, ?, ?, ?, ?, ?)",
            entry.Id.ToString(),
            (int)entry.Action,
            entry.DocumentId?.ToString(),
            entry.Details,
            Format(entry.CreatedAtUtc),
            Format(entry.ModifiedAtUtc));
    }

    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        Guid? documentId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);

        var conditions = new List<string>();
        var args = new List<object>();

        if (documentId is not null)
        {
            conditions.Add("document_id = ?");
            args.Add(documentId.Value.ToString());
        }
        if (fromUtc is not null)
        {
            // ISO-8601 "O" text sorts identically to chronological order here because every
            // CreatedAtUtc is DateTimeOffset.UtcNow (always +00:00) — see Entity's ctor.
            conditions.Add("created_at_utc >= ?");
            args.Add(Format(fromUtc.Value));
        }
        if (toUtc is not null)
        {
            conditions.Add("created_at_utc <= ?");
            args.Add(Format(toUtc.Value));
        }

        var sql = "SELECT * FROM audit_log_entries";
        if (conditions.Count > 0)
            sql += " WHERE " + string.Join(" AND ", conditions);
        sql += " ORDER BY created_at_utc";

        var rows = await connection.QueryAsync<AuditLogEntryRow>(sql, [.. args]);
        return rows.Select(ToEntity).ToList();
    }

    private static AuditLogEntry ToEntity(AuditLogEntryRow row)
    {
        var entity = EntityMaterializer.Create<AuditLogEntry>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(AuditLogEntry.Action), (AuditAction)row.Action);
        EntityMaterializer.Set(entity, nameof(AuditLogEntry.DocumentId), row.DocumentId is null ? null : Guid.Parse(row.DocumentId));
        EntityMaterializer.Set(entity, nameof(AuditLogEntry.Details), row.Details);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
