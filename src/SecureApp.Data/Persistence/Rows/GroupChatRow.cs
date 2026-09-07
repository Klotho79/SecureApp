using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>group_chats</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("group_chats")]
internal sealed class GroupChatRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("founder_public_key")] public byte[] FounderPublicKey { get; set; } = [];
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
