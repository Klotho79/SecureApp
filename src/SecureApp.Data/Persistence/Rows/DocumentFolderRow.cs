using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>document_folders</c> — see <see cref="EncryptionKeyMetadataRow"/> for why this exists.</summary>
[Table("document_folders")]
internal sealed class DocumentFolderRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("parent_folder_id")] public string? ParentFolderId { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
