using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>work_assignments</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("work_assignments")]
internal sealed class WorkAssignmentRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("date")] public string Date { get; set; } = string.Empty;
    [Column("type")] public int Type { get; set; }
    [Column("start_time")] public string? StartTime { get; set; }
    [Column("end_time")] public string? EndTime { get; set; }
    [Column("workplace_id")] public string? WorkplaceId { get; set; }
    [Column("workplace_name")] public string? WorkplaceName { get; set; }
    [Column("on_call_workplace_name")] public string? OnCallWorkplaceName { get; set; }
    [Column("note")] public string? Note { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
