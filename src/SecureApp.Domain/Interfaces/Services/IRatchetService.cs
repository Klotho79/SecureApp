using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Pure Double Ratchet protocol (Signal-style, ML-KEM standing in for X25519 in the DH step) — no
/// persistence or transport concerns. Reads/writes ratchet state via
/// <c>IRatchetSessionStateRepository</c>/<c>ISecureVaultKeyStore</c> internally; callers only ever
/// deal in plaintext bytes and session ids.
/// </summary>
public interface IRatchetService
{
    /// <summary>Initiator side: encapsulates against the peer's identity public key to establish the initial root key. Returns the ML-KEM ciphertext that must reach the peer (via a future transport) for them to call <see cref="CompleteHandshakeAsync"/>.</summary>
    Task<byte[]> InitiateHandshakeAsync(Guid sessionId, byte[] peerIdentityPublicKey, CancellationToken ct = default);

    /// <summary>Responder side: decapsulates the initiator's KEM ciphertext to derive the same initial root key.</summary>
    Task CompleteHandshakeAsync(Guid sessionId, byte[] kemCipherText, byte[] peerIdentityPublicKey, CancellationToken ct = default);

    Task<(RatchetMessageHeader Header, EncryptedPayload Payload)> RatchetEncryptAsync(Guid sessionId, ReadOnlyMemory<byte> plaintext, CancellationToken ct = default);

    /// <summary>Steps the receiving chain (or, for an out-of-order message, looks up a previously skipped key) and decrypts.</summary>
    Task<byte[]> RatchetDecryptAsync(Guid sessionId, RatchetMessageHeader header, EncryptedPayload payload, CancellationToken ct = default);
}
