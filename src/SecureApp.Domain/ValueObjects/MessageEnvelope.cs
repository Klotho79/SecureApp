namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Opaque wire-shape a future relay transport forwards without ever seeing plaintext — the relay
/// only ever routes ciphertext plus the ratchet header. Reuses <see cref="EncryptedPayload"/> for
/// the ciphertext body; note its <c>KeyId</c> carries the owning <see cref="SessionId"/> here
/// (correlating which session's ratchet state must decrypt it), not a vault-resolvable key as it
/// does for documents — a deliberate reuse, not the same meaning in both places.
///
/// <see cref="AttachmentLibraryFileId"/>/<see cref="AttachmentFileName"/> reference a file in the
/// community's shared library (<c>ISharedLibraryService</c>), not a local <see cref="AttachmentDocumentId"/>
/// — a relay-global id both sender and recipient can independently resolve, unlike a local
/// <c>Document.Id</c> which only ever exists on the device that imported it. This is the actual
/// mechanism a chat attachment reaches its recipient; <see cref="AttachmentDocumentId"/> remains for
/// same-device correlation only and was never itself cross-device-resolvable.
/// </summary>
public sealed record MessageEnvelope(
    Guid SessionId,
    Guid SenderUserId,
    RatchetMessageHeader Header,
    EncryptedPayload Payload,
    Guid? AttachmentDocumentId,
    Guid? AttachmentLibraryFileId = null,
    string? AttachmentFileName = null);
