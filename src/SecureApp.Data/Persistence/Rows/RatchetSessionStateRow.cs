using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>ratchet_session_states</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("ratchet_session_states")]
internal sealed class RatchetSessionStateRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("send_message_number")] public int SendMessageNumber { get; set; }
    [Column("receive_message_number")] public int ReceiveMessageNumber { get; set; }
    [Column("previous_send_chain_length")] public int PreviousSendChainLength { get; set; }
    [Column("current_send_chain_public_key")] public byte[] CurrentSendChainPublicKey { get; set; } = [];
    [Column("remote_ratchet_public_key")] public byte[]? RemoteRatchetPublicKey { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
