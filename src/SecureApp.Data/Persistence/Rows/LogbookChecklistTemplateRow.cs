using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>logbook_checklist_templates</c> — see <see cref="DocumentRow"/> for why this exists. <see cref="ItemsJson"/> is a JSON-serialized <c>string[]</c>, not a child table — see <c>LogbookChecklistTemplate</c>'s own remarks on why.</summary>
[Table("logbook_checklist_templates")]
internal sealed class LogbookChecklistTemplateRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("items_json")] public string ItemsJson { get; set; } = "[]";
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
