namespace SecureApp.Domain.Enums;

public enum KeyPurpose
{
    DocumentEncryption,
    Signing,

    /// <summary>Local-storage re-encryption key for chat message bodies (see <c>Message.Payload</c>'s remarks) — separate from <see cref="DocumentEncryption"/> so rotating one never affects the other.</summary>
    MessageStorage,

    /// <summary>
    /// The device's own long-lived ML-KEM identity key pair for chat (see <c>ChatSession.LocalIdentityKeyId</c>'s
    /// remarks). One shared key reused across every session, not minted per-session — a peer must
    /// already hold this key's public half (via <c>IMessagingService.GetLocalIdentityPublicKeyAsync</c>,
    /// exchanged out-of-band) before they can encapsulate a handshake against it.
    /// </summary>
    ChatIdentity
}
