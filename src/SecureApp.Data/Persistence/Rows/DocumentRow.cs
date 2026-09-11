using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>documents</c> — see <see cref="EncryptionKeyMetadataRow"/> for why this exists.</summary>
[Table("documents")]
internal sealed class DocumentRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("title")] public string Title { get; set; } = string.Empty;
    [Column("file_name")] public string FileName { get; set; } = string.Empty;
    [Column("document_type")] public int DocumentType { get; set; }
    [Column("original_size_bytes")] public long OriginalSizeBytes { get; set; }
    [Column("content_hash_algorithm")] public int ContentHashAlgorithm { get; set; }
    [Column("content_hash_hex")] public string ContentHashHex { get; set; } = string.Empty;
    [Column("encryption_key_id")] public string EncryptionKeyId { get; set; } = string.Empty;
    [Column("encryption_algorithm")] public int EncryptionAlgorithm { get; set; }
    [Column("cipher_text")] public byte[] CipherText { get; set; } = [];
    [Column("nonce")] public byte[] Nonce { get; set; } = [];
    [Column("auth_tag")] public byte[] AuthTag { get; set; } = [];
    [Column("folder_id")] public string? FolderId { get; set; }
    [Column("source_library_file_id")] public string? SourceLibraryFileId { get; set; }
    [Column("is_favorite")] public int IsFavorite { get; set; }
    [Column("tags")] public string Tags { get; set; } = "[]";
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
