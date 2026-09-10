using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>logbook_procedure_entries</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("logbook_procedure_entries")]
internal sealed class LogbookProcedureEntryRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("procedure_type_id")] public string ProcedureTypeId { get; set; } = string.Empty;
    [Column("level")] public int Level { get; set; }
    [Column("performed_at_utc")] public string PerformedAtUtc { get; set; } = string.Empty;
    [Column("note")] public string? Note { get; set; }
    [Column("place")] public string? Place { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
