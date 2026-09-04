using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>
/// sqlite-net's read-side row shape for <c>encryption_key_metadata</c>. Exists only
/// because the ORM's attribute-based mapping needs a type it can construct with a
/// public parameterless constructor and public settable properties — the Domain
/// entity deliberately has neither (see <see cref="EntityMaterializer"/>). Writes go
/// through hand-written parameterized SQL instead of this type's own INSERT/UPDATE
/// support, so it only needs to round-trip what a SELECT returns.
/// </summary>
[Table("encryption_key_metadata")]
internal sealed class EncryptionKeyMetadataRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("algorithm")] public int Algorithm { get; set; }
    [Column("purpose")] public int Purpose { get; set; }
    [Column("is_active")] public int IsActive { get; set; }
    [Column("rotated_at_utc")] public string? RotatedAtUtc { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
