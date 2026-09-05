using SecureApp.Domain.Entities;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Orchestrates <c>IRatchetService</c> + the chat repositories + <c>IAuditLogger</c> — the
/// messaging-module counterpart of <c>IDocumentImportService</c>. Never calls <c>IMessageTransport</c>
/// itself: <see cref="SendMessageAsync"/> only encrypts and persists a message as
/// <see cref="Enums.MessageStatus.Pending"/>; handing it to a connected transport is a later,
/// Presentation-layer concern once a real <c>IMessageTransport</c> implementation exists.
/// </summary>
public interface IMessagingService
{
    /// <summary>
    /// This device's own long-lived chat identity public key (<c>KeyPurpose.ChatIdentity</c>,
    /// minted on first use, reused for every session) — what must be given to a peer out-of-band
    /// before they can call <see cref="CreateSessionAsync"/> against this device.
    /// </summary>
    Task<byte[]> GetLocalIdentityPublicKeyAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds an existing non-Closed session with this exact peer identity, if any. Callers
    /// (New Chat's "paste/scan a contact card or invite" flow) should check this BEFORE calling
    /// <see cref="CreateSessionAsync"/>/<see cref="AcceptSessionAsync"/> and just open the existing
    /// session instead of starting a fresh handshake — those two methods themselves always mint a
    /// new session unconditionally (kept that way deliberately, so their own well-covered behavior
    /// and test suite stay untouched); this is the policy check that decides whether to call them
    /// at all. See IChatSessionRepository.GetByPeerPublicKeyAsync's own remarks for why this exists.
    /// </summary>
    Task<ChatSession?> FindExistingSessionAsync(byte[] peerIdentityPublicKey, CancellationToken ct = default);

    /// <summary>
    /// Initiator side: creates the session (using our shared device identity key — see
    /// <see cref="GetLocalIdentityPublicKeyAsync"/>) and starts the Double Ratchet handshake. The
    /// returned <c>HandshakeCipherText</c> must reach the peer (via a future transport) for
    /// <see cref="AcceptSessionAsync"/> to complete it on their end — this pass has no transport to
    /// carry it, so the caller (test code or a future integration) is responsible for delivering it
    /// out-of-band.
    /// </summary>
    Task<(ChatSession Session, byte[] HandshakeCipherText)> CreateSessionAsync(string peerDisplayName, byte[] peerIdentityPublicKey, Guid peerRelayDeviceId, CancellationToken ct = default);

    /// <summary>Responder side: creates the session (using our shared device identity key) and completes the handshake using the initiator's <c>HandshakeCipherText</c>. Fully active immediately — no further step needed on this side.</summary>
    Task<ChatSession> AcceptSessionAsync(string peerDisplayName, byte[] peerIdentityPublicKey, Guid peerRelayDeviceId, byte[] handshakeCipherText, CancellationToken ct = default);

    /// <summary>
    /// Encrypts + persists as <see cref="Enums.MessageStatus.Pending"/>. The returned <c>Envelope</c> is the wire-format payload a future transport would actually send — this method itself never calls one.
    /// <paramref name="attachmentLibraryFileId"/>/<paramref name="attachmentFileName"/> reference a file already uploaded to the community's shared library (<c>ISharedLibraryService</c>) — the real cross-device attachment path, since <paramref name="attachmentDocumentId"/> only ever resolves on the sender's own device.
    /// </summary>
    Task<(Message Message, MessageEnvelope Envelope)> SendMessageAsync(Guid sessionId, ReadOnlyMemory<byte> plaintext, Guid? attachmentDocumentId = null, Guid? attachmentLibraryFileId = null, string? attachmentFileName = null, CancellationToken ct = default);

    /// <summary>Hands a received envelope (from a transport) to the ratchet for decryption, then persists it as Inbound.</summary>
    Task<Message> ReceiveMessageAsync(MessageEnvelope envelope, CancellationToken ct = default);

    /// <summary>On-demand plaintext decrypt, mirroring the document viewer's decrypt-on-demand pattern.</summary>
    Task<byte[]> DecryptMessageAsync(Guid messageId, CancellationToken ct = default);

    Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default);
}
