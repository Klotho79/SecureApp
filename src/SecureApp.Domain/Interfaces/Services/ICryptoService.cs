using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Cryptography abstraction, implemented in the Data layer with BouncyCastle.
/// Combines a post-quantum KEM (ML-KEM) for key establishment with AES-256-GCM
/// for bulk data, and ML-DSA for signatures. Domain and Presentation code never
/// touch raw key material or a specific crypto library directly.
/// </summary>
public interface ICryptoService
{
    Task<KeyPairReference> GenerateEncryptionKeyPairAsync(CancellationToken ct = default);
    Task<KeyPairReference> GenerateSigningKeyPairAsync(CancellationToken ct = default);

    /// <summary>Re-retrieves a previously generated encryption key pair's public half (e.g. to share a long-lived identity key with a peer out-of-band).</summary>
    Task<byte[]> GetEncryptionPublicKeyAsync(Guid keyId, CancellationToken ct = default);

    Task<EncryptedPayload> EncryptAsync(ReadOnlyMemory<byte> plaintext, Guid keyId, CancellationToken ct = default);
    Task<byte[]> DecryptAsync(EncryptedPayload payload, CancellationToken ct = default);

    Task<byte[]> SignAsync(ReadOnlyMemory<byte> data, Guid signingKeyId, CancellationToken ct = default);
    Task<bool> VerifyAsync(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, Guid signingKeyId, CancellationToken ct = default);

    Task<FileHash> ComputeHashAsync(ReadOnlyMemory<byte> data, HashAlgorithmKind algorithm = HashAlgorithmKind.Sha256, CancellationToken ct = default);

    /// <summary>
    /// ML-KEM encapsulation against a peer's raw public key (not one of our own stored keys) —
    /// the initiator side of a Double Ratchet handshake. Stateless: the peer's key is provided by
    /// the caller and never touches the vault.
    /// </summary>
    Task<(byte[] KemCipherText, byte[] SharedSecret)> EncapsulateAsync(byte[] peerPublicKey, CancellationToken ct = default);

    /// <summary>ML-KEM decapsulation using our own stored private key — the responder side of a Double Ratchet handshake.</summary>
    Task<byte[]> DecapsulateAsync(Guid ownKeyId, byte[] kemCipherText, CancellationToken ct = default);

    /// <summary>Generic HKDF key derivation, used for every Double Ratchet KDF step (root key update, chain key update, message key derivation).</summary>
    Task<byte[]> DeriveKeyAsync(byte[] inputKeyMaterial, byte[]? salt, string info, int outputLength, CancellationToken ct = default);

    /// <summary>
    /// AES-256-GCM encryption with a caller-supplied raw key rather than a vault-resolved one.
    /// Exists solely for the Double Ratchet's one-shot, never-persisted per-message keys
    /// (<c>IRatchetService</c>) — ordinary long-lived keys (documents, chat local storage) still
    /// always go through <see cref="EncryptAsync"/>'s vault-mediated path.
    /// <paramref name="correlationId"/> becomes the returned payload's <c>KeyId</c> for the
    /// caller's own bookkeeping; it is never used to resolve anything from the vault.
    /// </summary>
    Task<EncryptedPayload> EncryptWithKeyAsync(ReadOnlyMemory<byte> plaintext, Guid correlationId, byte[] key, CancellationToken ct = default);

    /// <summary>Symmetric counterpart to <see cref="EncryptWithKeyAsync"/> — decrypts with a caller-supplied raw key, ignoring <paramref name="payload"/>'s <c>KeyId</c>.</summary>
    Task<byte[]> DecryptWithKeyAsync(EncryptedPayload payload, byte[] key, CancellationToken ct = default);
}
