using System.Globalization;
using System.Text.Json;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="ILogbookChecklistRepository"/>
public sealed class LogbookChecklistRepository : ILogbookChecklistRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public LogbookChecklistRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<LogbookChecklistTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<LogbookChecklistTemplateRow>("SELECT * FROM logbook_checklist_templates WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<LogbookChecklistTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<LogbookChecklistTemplateRow>("SELECT * FROM logbook_checklist_templates ORDER BY name");
        return rows.Select(ToEntity).ToList();
    }

    public async Task AddAsync(LogbookChecklistTemplate template, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO logbook_checklist_templates (id, name, items_json, created_at_utc, modified_at_utc)
            VALUES (?, ?, ?, ?, ?)
            """,
            template.Id.ToString(),
            template.Name,
            JsonSerializer.Serialize(template.Items),
            Format(template.CreatedAtUtc),
            Format(template.ModifiedAtUtc));
    }

    public async Task UpdateAsync(LogbookChecklistTemplate template, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE logbook_checklist_templates
            SET name = ?, items_json = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            template.Name,
            JsonSerializer.Serialize(template.Items),
            Format(template.ModifiedAtUtc),
            template.Id.ToString());

        if (rowsAffected == 0)
            throw new InvalidOperationException($"Logbook checklist template '{template.Id}' was not found.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM logbook_checklist_templates WHERE id = ?", id.ToString());
    }

    private static LogbookChecklistTemplate ToEntity(LogbookChecklistTemplateRow row)
    {
        var entity = EntityMaterializer.Create<LogbookChecklistTemplate>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(LogbookChecklistTemplate.Name), row.Name);
        EntityMaterializer.Set(entity, nameof(LogbookChecklistTemplate.Items), JsonSerializer.Deserialize<List<string>>(row.ItemsJson) ?? []);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
