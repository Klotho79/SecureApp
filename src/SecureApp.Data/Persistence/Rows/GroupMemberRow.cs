using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>group_members</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("group_members")]
internal sealed class GroupMemberRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("group_chat_id")] public string GroupChatId { get; set; } = string.Empty;
    [Column("display_name")] public string DisplayName { get; set; } = string.Empty;
    [Column("public_key")] public byte[] PublicKey { get; set; } = [];
    [Column("relay_device_id")] public string RelayDeviceId { get; set; } = string.Empty;
    [Column("can_invite")] public int CanInvite { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
