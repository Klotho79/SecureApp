using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>users</c> — see <see cref="EncryptionKeyMetadataRow"/> for why this exists.</summary>
[Table("users")]
internal sealed class UserRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("display_name")] public string DisplayName { get; set; } = string.Empty;
    [Column("role")] public int Role { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
