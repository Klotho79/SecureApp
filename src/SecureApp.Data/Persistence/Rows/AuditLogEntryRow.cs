using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>audit_log_entries</c> — see <see cref="EncryptionKeyMetadataRow"/> for why this exists.</summary>
[Table("audit_log_entries")]
internal sealed class AuditLogEntryRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("action")] public int Action { get; set; }
    [Column("document_id")] public string? DocumentId { get; set; }
    [Column("details")] public string? Details { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
