using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IWorkAssignmentRepository"/>
public sealed class WorkAssignmentRepository : IWorkAssignmentRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public WorkAssignmentRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<WorkAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<WorkAssignmentRow>("SELECT * FROM work_assignments WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<WorkAssignment>> GetByDateRangeAsync(DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<WorkAssignmentRow>(
            "SELECT * FROM work_assignments WHERE date >= ? AND date <= ? ORDER BY date ASC",
            FormatDate(fromInclusive), FormatDate(toInclusive));
        return rows.Select(ToEntity).ToList();
    }

    public async Task AddAsync(WorkAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO work_assignments (
                id, date, type, start_time, end_time, workplace_id, workplace_name, note,
                created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            assignment.Id.ToString(),
            FormatDate(assignment.Date),
            (int)assignment.Type,
            FormatTime(assignment.StartTime),
            FormatTime(assignment.EndTime),
            assignment.WorkplaceId?.ToString(),
            assignment.WorkplaceName,
            assignment.Note,
            Format(assignment.CreatedAtUtc),
            Format(assignment.ModifiedAtUtc));
    }

    public async Task UpdateAsync(WorkAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            UPDATE work_assignments
            SET date = ?, type = ?, start_time = ?, end_time = ?, workplace_id = ?, workplace_name = ?, note = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            FormatDate(assignment.Date),
            (int)assignment.Type,
            FormatTime(assignment.StartTime),
            FormatTime(assignment.EndTime),
            assignment.WorkplaceId?.ToString(),
            assignment.WorkplaceName,
            assignment.Note,
            Format(assignment.ModifiedAtUtc),
            assignment.Id.ToString());
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM work_assignments WHERE id = ?", id.ToString());
    }

    private static WorkAssignment ToEntity(WorkAssignmentRow row)
    {
        var entity = EntityMaterializer.Create<WorkAssignment>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(WorkAssignment.Date), DateOnly.ParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        EntityMaterializer.Set(entity, nameof(WorkAssignment.Type), (AssignmentType)row.Type);
        EntityMaterializer.Set(entity, nameof(WorkAssignment.StartTime), ParseTime(row.StartTime));
        EntityMaterializer.Set(entity, nameof(WorkAssignment.EndTime), ParseTime(row.EndTime));
        EntityMaterializer.Set(entity, nameof(WorkAssignment.WorkplaceId), row.WorkplaceId is null ? null : (Guid?)Guid.Parse(row.WorkplaceId));
        EntityMaterializer.Set(entity, nameof(WorkAssignment.WorkplaceName), row.WorkplaceName);
        EntityMaterializer.Set(entity, nameof(WorkAssignment.Note), row.Note);
        return entity;
    }

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? FormatTime(TimeOnly? value) => value?.ToString("HH:mm", CultureInfo.InvariantCulture);
    private static TimeOnly? ParseTime(string? value) => value is null ? null : TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
