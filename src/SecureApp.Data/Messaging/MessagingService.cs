using SecureApp.Data.Persistence;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Data.Messaging;

/// <inheritdoc cref="IMessagingService"/>
public sealed class MessagingService : IMessagingService
{
    private readonly ICryptoService _crypto;
    private readonly IRatchetService _ratchet;
    private readonly IChatSessionRepository _sessionRepository;
    private readonly IRatchetSessionStateRepository _stateRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IEncryptionKeyMetadataRepository _keyMetadataRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogger _auditLogger;

    public MessagingService(
        ICryptoService crypto,
        IRatchetService ratchet,
        IChatSessionRepository sessionRepository,
        IRatchetSessionStateRepository stateRepository,
        IMessageRepository messageRepository,
        IEncryptionKeyMetadataRepository keyMetadataRepository,
        ICurrentUserService currentUser,
        IAuditLogger auditLogger)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _ratchet = ratchet ?? throw new ArgumentNullException(nameof(ratchet));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _keyMetadataRepository = keyMetadataRepository ?? throw new ArgumentNullException(nameof(keyMetadataRepository));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    }

    public async Task<byte[]> GetLocalIdentityPublicKeyAsync(CancellationToken ct = default)
    {
        var keyId = await GetOrCreateLocalIdentityKeyIdAsync(ct);
        return await _crypto.GetEncryptionPublicKeyAsync(keyId, ct);
    }

    public Task<ChatSession?> FindExistingSessionAsync(byte[] peerIdentityPublicKey, CancellationToken ct = default)
        => _sessionRepository.GetByPeerPublicKeyAsync(peerIdentityPublicKey, ct);

    public async Task<(ChatSession Session, byte[] HandshakeCipherText)> CreateSessionAsync(string peerDisplayName, byte[] peerIdentityPublicKey, Guid peerRelayDeviceId, CancellationToken ct = default)
    {
        await CloseAnyExistingSessionsAsync(peerIdentityPublicKey, ct);

        var localIdentityKeyId = await GetOrCreateLocalIdentityKeyIdAsync(ct);
        var localIdentityPublicKey = await _crypto.GetEncryptionPublicKeyAsync(localIdentityKeyId, ct);

        var session = new ChatSession(peerDisplayName, peerIdentityPublicKey, localIdentityKeyId);
        session.LinkRelayDevice(peerRelayDeviceId);
        await _sessionRepository.AddAsync(session, ct);
        await InitializeStateAsync(session.Id, localIdentityPublicKey, ct);

        var handshakeCipherText = await _ratchet.InitiateHandshakeAsync(session.Id, peerIdentityPublicKey, ct);

        await _auditLogger.LogAsync(AuditAction.ChatSessionEstablished, details: $"role=initiator;peer={peerDisplayName}", ct: ct);
        return (session, handshakeCipherText);
    }

    public async Task<ChatSession> AcceptSessionAsync(string peerDisplayName, byte[] peerIdentityPublicKey, Guid peerRelayDeviceId, byte[] handshakeCipherText, CancellationToken ct = default)
    {
        await CloseAnyExistingSessionsAsync(peerIdentityPublicKey, ct);

        var localIdentityKeyId = await GetOrCreateLocalIdentityKeyIdAsync(ct);
        var localIdentityPublicKey = await _crypto.GetEncryptionPublicKeyAsync(localIdentityKeyId, ct);

        var session = new ChatSession(peerDisplayName, peerIdentityPublicKey, localIdentityKeyId);
        session.LinkRelayDevice(peerRelayDeviceId);
        await _sessionRepository.AddAsync(session, ct);
        await InitializeStateAsync(session.Id, localIdentityPublicKey, ct);

        await _ratchet.CompleteHandshakeAsync(session.Id, handshakeCipherText, peerIdentityPublicKey, ct);
        session.Activate();
        await _sessionRepository.UpdateAsync(session, ct);

        await _auditLogger.LogAsync(AuditAction.ChatSessionEstablished, details: $"role=responder;peer={peerDisplayName}", ct: ct);
        return session;
    }

    /// <summary>
    /// Enforces "at most one non-Closed <see cref="ChatSession"/> per peer identity key" at the
    /// root, in the one place every session-creating path already funnels through, rather than
    /// relying on every caller to check-then-act correctly (2026-09-07, a real bug caught live):
    /// two independent pairing paths racing for the same peer — say, a manual 1:1 pairing and a
    /// group's tie-break auto-pair, both firing around the same time — could each see "no existing
    /// session" via <see cref="FindExistingSessionAsync"/> and both go on to create one, leaving TWO
    /// valid sessions for the same peer with independently-derived ratchet state. Nothing forced the
    /// sender's and receiver's own session picks to agree after that — each side could keep
    /// "successfully" auto-healing against its own copy while decrypting every single message from
    /// the other side failed forever, since they were never actually the same session pair. Closing
    /// whatever's already there FIRST, unconditionally, makes creating a session for a peer always
    /// supersede anything before it — for every caller, not just the ones that remembered to check.
    /// A no-op in the common case (the caller already checked and found nothing, so there's usually
    /// nothing here to close); the loop only matters when a race actually happened.
    /// </summary>
    private async Task CloseAnyExistingSessionsAsync(byte[] peerIdentityPublicKey, CancellationToken ct)
    {
        while (await _sessionRepository.GetByPeerPublicKeyAsync(peerIdentityPublicKey, ct) is { } existing)
            await CloseSessionAsync(existing.Id, ct);
    }

    private async Task InitializeStateAsync(Guid sessionId, byte[] localIdentityPublicKey, CancellationToken ct)
    {
        var state = new RatchetSessionState(localIdentityPublicKey);
        EntityMaterializer.Set(state, nameof(Entity.Id), sessionId);
        await _stateRepository.UpsertAsync(state, ct);
    }

    /// <summary>
    /// Reuses the vault's current active <see cref="KeyPurpose.ChatIdentity"/> key, minting one on
    /// first use — unlike <see cref="GetOrCreateActiveMessageStorageKeyIdAsync"/>, this key is a
    /// long-lived per-device identity, not per-purpose bulk-encryption key, but it's the same
    /// "mint once, correlate metadata.Id to the vault KeyId" pattern either way.
    /// </summary>
    private async Task<Guid> GetOrCreateLocalIdentityKeyIdAsync(CancellationToken ct)
    {
        var activeMetadata = await _keyMetadataRepository.GetActiveKeyAsync(KeyPurpose.ChatIdentity, ct);
        if (activeMetadata is not null)
            return activeMetadata.Id;

        var keyPair = await _crypto.GenerateEncryptionKeyPairAsync(ct);

        var metadata = new EncryptionKeyMetadata(keyPair.Algorithm, KeyPurpose.ChatIdentity);
        EntityMaterializer.Set(metadata, nameof(Entity.Id), keyPair.KeyId);

        await _keyMetadataRepository.AddAsync(metadata, ct);
        await _auditLogger.LogAsync(AuditAction.EncryptionKeyGenerated, details: $"purpose={KeyPurpose.ChatIdentity}", ct: ct);

        return metadata.Id;
    }

    public async Task<(Message Message, MessageEnvelope Envelope)> SendMessageAsync(Guid sessionId, ReadOnlyMemory<byte> plaintext, Guid? attachmentDocumentId = null, Guid? attachmentLibraryFileId = null, string? attachmentFileName = null, Guid? groupChatId = null, Guid? groupMessageId = null, CancellationToken ct = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, ct) ?? throw new ChatSessionNotFoundException(sessionId);
        if (session.State == ChatSessionState.Closed)
            throw new ChatSessionClosedException(sessionId);

        // Ratchet-encrypt to produce the wire envelope a future transport would send. Its
        // ephemeral message key is never persisted (that's the ratchet's forward secrecy) — the
        // wire payload only ever exists transiently, here and in the returned envelope.
        var (header, wirePayload) = await _ratchet.RatchetEncryptAsync(sessionId, plaintext, ct);
        var envelope = new MessageEnvelope(sessionId, _currentUser.Current.Id, header, wirePayload, attachmentDocumentId, attachmentLibraryFileId, attachmentFileName, groupChatId, groupMessageId);

        // Local storage copy: re-encrypted under a normal, vault-mediated key so it can be
        // redecrypted on demand at any time — see Message's remarks.
        var storageKeyId = await GetOrCreateActiveMessageStorageKeyIdAsync(ct);
        var storedPayload = await _crypto.EncryptAsync(plaintext, storageKeyId, ct);

        var message = new Message(sessionId, MessageDirection.Outbound, header, storedPayload, attachmentDocumentId, attachmentLibraryFileId, attachmentFileName, groupChatId, groupMessageId);
        await _messageRepository.AddAsync(message, ct);

        session.NoteRatcheted();
        await _sessionRepository.UpdateAsync(session, ct);

        await _auditLogger.LogAsync(AuditAction.MessageSent, attachmentDocumentId, details: $"sessionId={sessionId}", ct: ct);
        return (message, envelope);
    }

    public async Task<Message> ReceiveMessageAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var session = await _sessionRepository.GetByIdAsync(envelope.SessionId, ct) ?? throw new ChatSessionNotFoundException(envelope.SessionId);
        if (session.State == ChatSessionState.Closed)
            throw new ChatSessionClosedException(envelope.SessionId);

        // Idempotency guard (2026-09-07) — a real bug surfaced this live: if the exact same
        // envelope somehow reaches this method twice (multiple live listeners on the transport's
        // singleton EnvelopeReceived event — e.g. a stale ViewModel instance that never got its
        // StopListening call, on top of the cross-contamination ChatViewModel/GroupChatViewModel
        // fix below — or a transport-level duplicate delivery), decrypting it a second time fails
        // outright: a Double Ratchet message key is one-time-use by design, that's the whole point
        // of forward secrecy. Recognized by its exact wire header — DhPublicKey + MessageNumber
        // together uniquely identify one message within one sending chain — so a genuine duplicate
        // just returns the already-stored row instead of re-attempting the ratchet.
        var alreadyReceived = await _messageRepository.GetBySessionAsync(envelope.SessionId, ct: ct);
        var duplicate = alreadyReceived.FirstOrDefault(m =>
            m.Direction == MessageDirection.Inbound &&
            m.Header.MessageNumber == envelope.Header.MessageNumber &&
            m.Header.DhPublicKey.AsSpan().SequenceEqual(envelope.Header.DhPublicKey));
        if (duplicate is not null)
            return duplicate;

        // Decrypting the wire envelope both verifies authenticity and advances the ratchet's
        // receive chain — this must happen exactly once per message, which is also why the
        // decrypted plaintext (not the wire ciphertext) is what gets re-encrypted for storage.
        var plaintext = await _ratchet.RatchetDecryptAsync(envelope.SessionId, envelope.Header, envelope.Payload, ct);

        var storageKeyId = await GetOrCreateActiveMessageStorageKeyIdAsync(ct);
        var storedPayload = await _crypto.EncryptAsync(plaintext, storageKeyId, ct);

        var message = new Message(envelope.SessionId, MessageDirection.Inbound, envelope.Header, storedPayload, envelope.AttachmentDocumentId, envelope.AttachmentLibraryFileId, envelope.AttachmentFileName, envelope.GroupChatId, envelope.GroupMessageId);
        message.MarkDelivered();
        await _messageRepository.AddAsync(message, ct);

        session.NoteRatcheted();
        await _sessionRepository.UpdateAsync(session, ct);

        await _auditLogger.LogAsync(AuditAction.MessageReceived, envelope.AttachmentDocumentId, details: $"sessionId={envelope.SessionId}", ct: ct);
        return message;
    }

    public async Task<byte[]> DecryptMessageAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await _messageRepository.GetByIdAsync(messageId, ct) ?? throw new MessageNotFoundException(messageId);
        return await _crypto.DecryptAsync(message.Payload, ct);
    }

    public async Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, ct) ?? throw new ChatSessionNotFoundException(sessionId);
        session.Close();
        await _sessionRepository.UpdateAsync(session, ct);
        await _auditLogger.LogAsync(AuditAction.ChatSessionClosed, details: $"sessionId={sessionId}", ct: ct);
    }

    /// <summary>Reuses the vault's current active <see cref="KeyPurpose.MessageStorage"/> key, minting one on first use — mirrors <c>DocumentImportService</c>'s identical pattern for <see cref="KeyPurpose.DocumentEncryption"/>.</summary>
    private async Task<Guid> GetOrCreateActiveMessageStorageKeyIdAsync(CancellationToken ct)
    {
        var activeMetadata = await _keyMetadataRepository.GetActiveKeyAsync(KeyPurpose.MessageStorage, ct);
        if (activeMetadata is not null)
            return activeMetadata.Id;

        var keyPair = await _crypto.GenerateEncryptionKeyPairAsync(ct);

        var metadata = new EncryptionKeyMetadata(keyPair.Algorithm, KeyPurpose.MessageStorage);
        EntityMaterializer.Set(metadata, nameof(Entity.Id), keyPair.KeyId);

        await _keyMetadataRepository.AddAsync(metadata, ct);
        await _auditLogger.LogAsync(AuditAction.EncryptionKeyGenerated, details: $"purpose={KeyPurpose.MessageStorage}", ct: ct);

        return metadata.Id;
    }
}
