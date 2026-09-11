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

    /// <summary>
    /// Set when this row is one leg of a group message's pairwise fan-out (2026-09-07) — see
    /// <see cref="GroupChat"/>'s own remarks on the crypto design. Null for an ordinary 1:1 message.
    /// Lets the 1:1 <c>ChatViewModel</c> thread for <see cref="ChatSessionId"/> filter group traffic
    /// back out (group messages display in their own group thread instead, not mixed into a
    /// specific member's direct conversation) while still physically traveling over that member's
    /// pairwise ratchet.
    /// </summary>
    public Guid? GroupChatId { get; private set; }

    /// <summary>Shared across every fan-out copy of the SAME logical group message (one per other member) — lets the sender's own group thread collapse its own N outbound rows back into the one bubble a recipient only ever sees once. Null whenever <see cref="GroupChatId"/> is null.</summary>
    public Guid? GroupMessageId { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }

    /// <summary>
    /// Set (2026-09-10) for a message that carries app-internal machinery over the same E2EE
    /// pairwise ratchet — currently just the shared library key auto-offer (see
    /// <c>SharedLibraryKeySync</c> in the Presentation layer): the user's own objection was "pokud
    /// je v chatu kde maji pristup uzivatele proc nemaji klic?" (if they're already trusted enough
    /// to be in a chat, why don't they have the key?) — answered by riding the already-established,
    /// already-authenticated ratchet instead of a manual copy/paste. Never shown in
    /// <c>ChatViewModel</c>/<c>GroupChatViewModel</c>'s own thread — filtered out the exact same way
    /// <see cref="GroupChatId"/> already is for the opposite reason (belongs elsewhere, not this
    /// thread); this one belongs nowhere visible at all.
    /// </summary>
    public bool IsSystemPayload { get; private set; }

    /// <summary>
    /// A relay-global id shared by every device's copy of the SAME logical message (2026-09-11) —
    /// the sender mints it once, the recipient copies it off the envelope, unlike <see cref="Id"/>
    /// which is a fresh per-device GUID. Lets a "delete this message" command reference one message
    /// across devices (see <c>MessageDeletionSync</c>) even though the local <see cref="Id"/> differs
    /// on each end. Null on messages sent before this existed — those can only be deleted locally,
    /// never propagated. For a group message, correlation across the fan-out uses <see cref="GroupMessageId"/>
    /// instead (already shared across legs), so this is primarily the 1:1 correlation id.
    /// </summary>
    public Guid? OriginMessageId { get; private set; }

    /// <summary>
    /// The RBAC <see cref="Enums.Role"/> the sender held when they sent this message (2026-09-11),
    /// carried on the envelope and stored on the recipient's copy — the one piece of information a
    /// deletion RBAC check needs that isn't otherwise knowable about someone else's message: whether
    /// a Modifier may delete it (a Modifier may delete a Viewer's messages, but not another
    /// Modifier's or an Admin's — see <c>RoleAccessPolicy.CanDeleteMessage</c>). Null on messages
    /// sent before this existed; the policy degrades safely (a Modifier can't delete an unknown-role
    /// message that isn't their own). Consistent with this app's existing cooperative role model —
    /// roles are self-declared in Settings, not server-enforced.
    /// </summary>
    public Role? SenderRole { get; private set; }

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
        string? attachmentFileName = null,
        Guid? groupChatId = null,
        Guid? groupMessageId = null,
        bool isSystemPayload = false,
        Guid? originMessageId = null,
        Role? senderRole = null)
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
        GroupChatId = groupChatId;
        GroupMessageId = groupMessageId;
        IsSystemPayload = isSystemPayload;
        OriginMessageId = originMessageId;
        SenderRole = senderRole;
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
