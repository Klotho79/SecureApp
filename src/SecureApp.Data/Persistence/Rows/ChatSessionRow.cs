using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>chat_sessions</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("chat_sessions")]
internal sealed class ChatSessionRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("peer_display_name")] public string PeerDisplayName { get; set; } = string.Empty;
    [Column("peer_identity_public_key")] public byte[] PeerIdentityPublicKey { get; set; } = [];
    [Column("local_identity_key_id")] public string LocalIdentityKeyId { get; set; } = string.Empty;
    [Column("state")] public int State { get; set; }
    [Column("last_ratcheted_at_utc")] public string? LastRatchetedAtUtc { get; set; }
    [Column("peer_relay_device_id")] public string? PeerRelayDeviceId { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
