using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="ILogbookProcedureEntryRepository"/>
public sealed class LogbookProcedureEntryRepository : ILogbookProcedureEntryRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public LogbookProcedureEntryRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<LogbookProcedureEntry>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<LogbookProcedureEntryRow>("SELECT * FROM logbook_procedure_entries ORDER BY performed_at_utc DESC");
        return rows.Select(ToEntity).ToList();
    }

    public async Task AddAsync(LogbookProcedureEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO logbook_procedure_entries (id, procedure_type_id, level, performed_at_utc, note, created_at_utc, modified_at_utc)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            entry.Id.ToString(),
            entry.ProcedureTypeId.ToString(),
            (int)entry.Level,
            Format(entry.PerformedAtUtc),
            entry.Note,
            Format(entry.CreatedAtUtc),
            Format(entry.ModifiedAtUtc));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM logbook_procedure_entries WHERE id = ?", id.ToString());
    }

    private static LogbookProcedureEntry ToEntity(LogbookProcedureEntryRow row)
    {
        var entity = EntityMaterializer.Create<LogbookProcedureEntry>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(LogbookProcedureEntry.ProcedureTypeId), Guid.Parse(row.ProcedureTypeId));
        EntityMaterializer.Set(entity, nameof(LogbookProcedureEntry.Level), (LogbookCompetenceLevel)row.Level);
        EntityMaterializer.Set(entity, nameof(LogbookProcedureEntry.PerformedAtUtc), Parse(row.PerformedAtUtc));
        EntityMaterializer.Set(entity, nameof(LogbookProcedureEntry.Note), row.Note);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
