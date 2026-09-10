using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>logbook_procedure_types</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("logbook_procedure_types")]
internal sealed class LogbookProcedureTypeRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("abbreviation")] public string Abbreviation { get; set; } = string.Empty;
    [Column("category")] public int Category { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
