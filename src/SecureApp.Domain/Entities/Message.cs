using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One chat message. Like <see cref="Document"/>, this entity never holds plaintext.
///
/// <see cref="Payload"/> is <b>not</b> the wire-format Double Ratchet ciphertext — Double Ratchet
/// message keys are one-shot and discarded immediately after use (that's the whole point of
/// forward secrecy), so they cannot re-decrypt anything later for redisplay. Instead, exactly
/// like <c>Document.EncryptedContent</c>, <see cref="Payload"/> is re-encrypted under a normal,
/// vault-mediated <c>KeyPurpose.MessageStorage</c> key via <c>ICryptoService.EncryptAsync</c>/
/// <c>DecryptAsync</c> — redecryptable on demand at any time. The Double Ratchet protects the
/// message only in transit; SQLCipher (the whole local database) protects it at rest, same as
/// every other entity in this app.
///
/// <see cref="Header"/> is the wire-format Double Ratchet header that accompanied this message
/// when it was actually ratchet-encrypted for transmission (outbound) or as received (inbound) —
/// kept for reference/future-transport use, not used to decrypt <see cref="Payload"/>.
/// </summary>
public sealed class Message : Entity
{
    public Guid ChatSessionId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public MessageStatus Status { get; private set; }
    public RatchetMessageHeader Header { get; private set; }
    public EncryptedPayload Payload { get; private set; }

    /// <summary>Links to an already-imported, already-encrypted <see cref="Document"/> sent as an attachment. The document's own ciphertext is untouched — this is just a reference. Only ever resolvable on the device that imported that document — see <see cref="AttachmentLibraryFileId"/> for the cross-device case.</summary>
    public Guid? AttachmentDocumentId { get; private set; }

    /// <summary>
    /// References a file in the community's shared library (<c>ISharedLibraryService</c>) sent as
    /// an attachment — a relay-global id both sender and recipient can independently resolve via
    /// <c>ISharedLibraryService.DownloadAndImportAsync</c>, unlike <see cref="AttachmentDocumentId"/>.
    /// This is the actual mechanism a real cross-device chat attachment works through.
    /// </summary>
    public Guid? AttachmentLibraryFileId { get; private set; }

    /// <summary>Original file name for a library attachment, carried alongside <see cref="AttachmentLibraryFileId"/> so the UI can show it before/without a metadata round trip.</summary>
    public string? AttachmentFileName { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }

    private Message()
    {
        // Reserved for materialization by persistence/serialization infrastructure.
        Header = null!;
        Payload = null!;
    }

    public Message(
        Guid chatSessionId,
        MessageDirection direction,
        RatchetMessageHeader header,
        EncryptedPayload payload,
        Guid? attachmentDocumentId = null,
        Guid? attachmentLibraryFileId = null,
        string? attachmentFileName = null)
    {
        if (chatSessionId == Guid.Empty)
            throw new ArgumentException("Chat session id cannot be empty.", nameof(chatSessionId));

        ChatSessionId = chatSessionId;
        Direction = direction;
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        AttachmentDocumentId = attachmentDocumentId;
        AttachmentLibraryFileId = attachmentLibraryFileId;
        AttachmentFileName = attachmentFileName;
        Status = MessageStatus.Pending;
    }

    public void MarkSent()
    {
        Status = MessageStatus.Sent;
        Touch();
    }

    public void MarkDelivered()
    {
        Status = MessageStatus.Delivered;
        DeliveredAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkRead()
    {
        Status = MessageStatus.Read;
        ReadAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkFailed()
    {
        Status = MessageStatus.Failed;
        Touch();
    }
}
