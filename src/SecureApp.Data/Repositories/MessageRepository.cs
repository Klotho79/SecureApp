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
                delivered_at_utc, read_at_utc, created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
        EntityMaterializer.Set(entity, nameof(Message.DeliveredAtUtc), row.DeliveredAtUtc is null ? null : (DateTimeOffset?)Parse(row.DeliveredAtUtc));
        EntityMaterializer.Set(entity, nameof(Message.ReadAtUtc), row.ReadAtUtc is null ? null : (DateTimeOffset?)Parse(row.ReadAtUtc));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
