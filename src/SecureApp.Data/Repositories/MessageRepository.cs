using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IMessageRepository"/>
public sealed class MessageRepository : IMessageRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public MessageRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<MessageRow>("SELECT * FROM messages WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<Message>> GetBySessionAsync(Guid chatSessionId, DateTimeOffset? sinceUtc = null, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = sinceUtc is null
            ? await connection.QueryAsync<MessageRow>("SELECT * FROM messages WHERE chat_session_id = ? ORDER BY created_at_utc", chatSessionId.ToString())
            : await connection.QueryAsync<MessageRow>("SELECT * FROM messages WHERE chat_session_id = ? AND created_at_utc >= ? ORDER BY created_at_utc", chatSessionId.ToString(), Format(sinceUtc.Value));
        return rows.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Message>> GetByGroupAsync(Guid groupChatId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<MessageRow>("SELECT * FROM messages WHERE group_chat_id = ? ORDER BY created_at_utc", groupChatId.ToString());
        return rows.Select(ToEntity).ToList();
    }

    public async Task<string> GetSessionSignatureAsync(Guid chatSessionId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM messages WHERE chat_session_id = ? AND group_chat_id IS NULL AND is_system_payload = 0", chatSessionId.ToString());
        var maxCreated = await connection.ExecuteScalarAsync<string>(
            "SELECT MAX(created_at_utc) FROM messages WHERE chat_session_id = ? AND group_chat_id IS NULL AND is_system_payload = 0", chatSessionId.ToString());
        return $"{count}:{maxCreated}";
    }

    public async Task<string> GetGroupSignatureAsync(Guid groupChatId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM messages WHERE group_chat_id = ? AND is_system_payload = 0", groupChatId.ToString());
        var maxCreated = await connection.ExecuteScalarAsync<string>(
            "SELECT MAX(created_at_utc) FROM messages WHERE group_chat_id = ? AND is_system_payload = 0", groupChatId.ToString());
        return $"{count}:{maxCreated}";
    }

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO messages (
                id, chat_session_id, direction, status,
                header_dh_public_key, header_previous_chain_length, header_message_number,
                payload_key_id, payload_algorithm, payload_cipher_text, payload_nonce, payload_auth_tag,
                attachment_document_id, attachment_library_file_id, attachment_file_name,
                group_chat_id, group_message_id, is_system_payload, origin_message_id, sender_role,
                delivered_at_utc, read_at_utc, created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            message.Id.ToString(),
            message.ChatSessionId.ToString(),
            (int)message.Direction,
            (int)message.Status,
            message.Header.DhPublicKey,
            message.Header.PreviousChainLength,
            message.Header.MessageNumber,
            message.Payload.KeyId.ToString(),
            (int)message.Payload.Algorithm,
            message.Payload.CipherText,
            message.Payload.Nonce,
            message.Payload.AuthTag,
            message.AttachmentDocumentId?.ToString(),
            message.AttachmentLibraryFileId?.ToString(),
            message.AttachmentFileName,
            message.GroupChatId?.ToString(),
            message.GroupMessageId?.ToString(),
            message.IsSystemPayload,
            message.OriginMessageId?.ToString(),
            message.SenderRole is null ? (int?)null : (int)message.SenderRole.Value,
            message.DeliveredAtUtc is null ? null : Format(message.DeliveredAtUtc.Value),
            message.ReadAtUtc is null ? null : Format(message.ReadAtUtc.Value),
            Format(message.CreatedAtUtc),
            Format(message.ModifiedAtUtc));
    }

    public async Task UpdateAsync(Message message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE messages
            SET status = ?, delivered_at_utc = ?, read_at_utc = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            (int)message.Status,
            message.DeliveredAtUtc is null ? null : Format(message.DeliveredAtUtc.Value),
            message.ReadAtUtc is null ? null : Format(message.ReadAtUtc.Value),
            Format(message.ModifiedAtUtc),
            message.Id.ToString());

        if (rowsAffected == 0)
            throw new MessageNotFoundException(message.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        // No MessageNotFoundException here (unlike UpdateAsync) — delete is idempotent by design: a
        // propagated delete can legitimately race a device that already removed the row, or target a
        // message that never reached this device at all.
        await connection.ExecuteAsync("DELETE FROM messages WHERE id = ?", id.ToString());
    }

    public async Task DeleteByCorrelationAsync(Guid correlationId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            "DELETE FROM messages WHERE origin_message_id = ? OR group_message_id = ?",
            correlationId.ToString(), correlationId.ToString());
    }

    /// <summary>
    /// Marks this device's OWN outbound copy of a message Delivered when the recipient's app has
    /// acknowledged receipt (2026-09-14, delivery receipts). Matches by cross-device correlation id
    /// (origin_message_id for 1:1, group_message_id for a group fan-out) and only upgrades a message
    /// that is still Pending/Sent (status &lt; Delivered) so a later ack never downgrades a Read
    /// message. Idempotent: a duplicate ack (or an ack for a message this device doesn't have) is a
    /// harmless no-op. Returns true if a row was actually upgraded, so the caller can refresh the UI.
    /// </summary>
    public async Task<bool> MarkDeliveredByCorrelationAsync(Guid correlationId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var now = Format(DateTimeOffset.UtcNow);
        var rows = await connection.ExecuteAsync(
            """
            UPDATE messages
            SET status = ?, delivered_at_utc = ?, modified_at_utc = ?
            WHERE (origin_message_id = ? OR group_message_id = ?)
              AND direction = ?
              AND status < ?
            """,
            (int)MessageStatus.Delivered, now, now,
            correlationId.ToString(), correlationId.ToString(),
            (int)MessageDirection.Outbound,
            (int)MessageStatus.Delivered);
        return rows > 0;
    }

    /// <summary>
    /// Marks the ONE outbound fan-out leg that went to a specific member Delivered (2026-09-14, group
    /// delivery receipts). A group message fans out one row per member (each a Message on that member's
    /// pairwise session, all sharing group_message_id); an ack arrives on that member's session, so
    /// matching by chat_session_id + correlation id upgrades only that member's leg — letting the sender
    /// show ✓✓ only once EVERY member's leg is Delivered (see <see cref="GetOutboundAggregateStatusAsync"/>).
    /// Also correct for a 1:1 message (a single leg on that session). Idempotent; returns true if upgraded.
    /// </summary>
    public async Task<bool> MarkDeliveredForSessionAsync(Guid chatSessionId, Guid correlationId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var now = Format(DateTimeOffset.UtcNow);
        var rows = await connection.ExecuteAsync(
            """
            UPDATE messages
            SET status = ?, delivered_at_utc = ?, modified_at_utc = ?
            WHERE chat_session_id = ?
              AND (origin_message_id = ? OR group_message_id = ?)
              AND direction = ?
              AND status < ?
            """,
            (int)MessageStatus.Delivered, now, now,
            chatSessionId.ToString(),
            correlationId.ToString(), correlationId.ToString(),
            (int)MessageDirection.Outbound,
            (int)MessageStatus.Delivered);
        return rows > 0;
    }

    /// <summary>
    /// The aggregate delivery status of an outbound message across all its fan-out legs (2026-09-14) —
    /// the MINIMUM status over every outbound row sharing this correlation id. So a group message reads
    /// Delivered only when EVERY member's leg is Delivered, and stays Sent while any leg is still
    /// un-acked; a 1:1 message has one leg so it's just that leg's status. Null if no such outbound row
    /// exists (e.g. a very old message with no correlation id).
    /// </summary>
    public async Task<MessageStatus?> GetOutboundAggregateStatusAsync(Guid correlationId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM messages WHERE (origin_message_id = ? OR group_message_id = ?) AND direction = ?",
            correlationId.ToString(), correlationId.ToString(), (int)MessageDirection.Outbound);
        if (count == 0) return null;

        var min = await connection.ExecuteScalarAsync<int>(
            "SELECT MIN(status) FROM messages WHERE (origin_message_id = ? OR group_message_id = ?) AND direction = ?",
            correlationId.ToString(), correlationId.ToString(), (int)MessageDirection.Outbound);
        return (MessageStatus)min;
    }

    public async Task ReassignSessionAsync(Guid oldChatSessionId, Guid newChatSessionId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            "UPDATE messages SET chat_session_id = ? WHERE chat_session_id = ?",
            newChatSessionId.ToString(), oldChatSessionId.ToString());
    }

    private static Message ToEntity(MessageRow row)
    {
        var entity = EntityMaterializer.Create<Message>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(Message.ChatSessionId), Guid.Parse(row.ChatSessionId));
        EntityMaterializer.Set(entity, nameof(Message.Direction), (MessageDirection)row.Direction);
        EntityMaterializer.Set(entity, nameof(Message.Status), (MessageStatus)row.Status);
        EntityMaterializer.Set(entity, nameof(Message.Header), new RatchetMessageHeader(row.HeaderDhPublicKey, row.HeaderPreviousChainLength, row.HeaderMessageNumber));
        EntityMaterializer.Set(entity, nameof(Message.Payload), new EncryptedPayload(
            Guid.Parse(row.PayloadKeyId), (EncryptionAlgorithm)row.PayloadAlgorithm, row.PayloadCipherText, row.PayloadNonce, row.PayloadAuthTag));
        EntityMaterializer.Set(entity, nameof(Message.AttachmentDocumentId), row.AttachmentDocumentId is null ? null : Guid.Parse(row.AttachmentDocumentId));
        EntityMaterializer.Set(entity, nameof(Message.AttachmentLibraryFileId), row.AttachmentLibraryFileId is null ? null : Guid.Parse(row.AttachmentLibraryFileId));
        EntityMaterializer.Set(entity, nameof(Message.AttachmentFileName), row.AttachmentFileName);
        EntityMaterializer.Set(entity, nameof(Message.GroupChatId), row.GroupChatId is null ? null : (Guid?)Guid.Parse(row.GroupChatId));
        EntityMaterializer.Set(entity, nameof(Message.GroupMessageId), row.GroupMessageId is null ? null : (Guid?)Guid.Parse(row.GroupMessageId));
        EntityMaterializer.Set(entity, nameof(Message.IsSystemPayload), row.IsSystemPayload);
        EntityMaterializer.Set(entity, nameof(Message.OriginMessageId), row.OriginMessageId is null ? null : (Guid?)Guid.Parse(row.OriginMessageId));
        EntityMaterializer.Set(entity, nameof(Message.SenderRole), row.SenderRole is null ? null : (Role?)(Role)row.SenderRole.Value);
        EntityMaterializer.Set(entity, nameof(Message.DeliveredAtUtc), row.DeliveredAtUtc is null ? null : (DateTimeOffset?)Parse(row.DeliveredAtUtc));
        EntityMaterializer.Set(entity, nameof(Message.ReadAtUtc), row.ReadAtUtc is null ? null : (DateTimeOffset?)Parse(row.ReadAtUtc));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
