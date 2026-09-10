using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>messages</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("messages")]
internal sealed class MessageRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("chat_session_id")] public string ChatSessionId { get; set; } = string.Empty;
    [Column("direction")] public int Direction { get; set; }
    [Column("status")] public int Status { get; set; }
    [Column("header_dh_public_key")] public byte[] HeaderDhPublicKey { get; set; } = [];
    [Column("header_previous_chain_length")] public int HeaderPreviousChainLength { get; set; }
    [Column("header_message_number")] public int HeaderMessageNumber { get; set; }
    [Column("payload_key_id")] public string PayloadKeyId { get; set; } = string.Empty;
    [Column("payload_algorithm")] public int PayloadAlgorithm { get; set; }
    [Column("payload_cipher_text")] public byte[] PayloadCipherText { get; set; } = [];
    [Column("payload_nonce")] public byte[] PayloadNonce { get; set; } = [];
    [Column("payload_auth_tag")] public byte[] PayloadAuthTag { get; set; } = [];
    [Column("attachment_document_id")] public string? AttachmentDocumentId { get; set; }
    [Column("attachment_library_file_id")] public string? AttachmentLibraryFileId { get; set; }
    [Column("attachment_file_name")] public string? AttachmentFileName { get; set; }
    [Column("group_chat_id")] public string? GroupChatId { get; set; }
    [Column("group_message_id")] public string? GroupMessageId { get; set; }
    [Column("is_system_payload")] public bool IsSystemPayload { get; set; }
    [Column("delivered_at_utc")] public string? DeliveredAtUtc { get; set; }
    [Column("read_at_utc")] public string? ReadAtUtc { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
