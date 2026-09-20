using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>contacts</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("contacts")]
internal sealed class ContactRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("display_name")] public string DisplayName { get; set; } = string.Empty;
    [Column("phone")] public string? Phone { get; set; }
    [Column("email")] public string? Email { get; set; }
    [Column("note")] public string? Note { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("linked_public_key")] public byte[]? LinkedPublicKey { get; set; }
    [Column("linked_relay_device_id")] public string? LinkedRelayDeviceId { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
