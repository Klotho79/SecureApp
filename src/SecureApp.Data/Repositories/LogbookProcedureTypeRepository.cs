using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="ILogbookProcedureTypeRepository"/>
public sealed class LogbookProcedureTypeRepository : ILogbookProcedureTypeRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public LogbookProcedureTypeRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<LogbookProcedureType?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<LogbookProcedureTypeRow>("SELECT * FROM logbook_procedure_types WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<LogbookProcedureType>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<LogbookProcedureTypeRow>("SELECT * FROM logbook_procedure_types ORDER BY category, name");
        return rows.Select(ToEntity).ToList();
    }

    public async Task AddAsync(LogbookProcedureType type, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO logbook_procedure_types (id, name, category, created_at_utc, modified_at_utc)
            VALUES (?, ?, ?, ?, ?)
            """,
            type.Id.ToString(),
            type.Name,
            (int)type.Category,
            Format(type.CreatedAtUtc),
            Format(type.ModifiedAtUtc));
    }

    public async Task UpdateAsync(LogbookProcedureType type, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE logbook_procedure_types
            SET name = ?, category = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            type.Name,
            (int)type.Category,
            Format(type.ModifiedAtUtc),
            type.Id.ToString());

        if (rowsAffected == 0)
            throw new InvalidOperationException($"Logbook procedure type '{type.Id}' was not found.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM logbook_procedure_types WHERE id = ?", id.ToString());
    }

    private static LogbookProcedureType ToEntity(LogbookProcedureTypeRow row)
    {
        var entity = EntityMaterializer.Create<LogbookProcedureType>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(LogbookProcedureType.Name), row.Name);
        EntityMaterializer.Set(entity, nameof(LogbookProcedureType.Category), (LogbookProcedureCategory)row.Category);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
