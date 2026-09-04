using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Opaque, self-describing ciphertext envelope produced by <c>ICryptoService</c>.
/// Domain and Presentation code pass this around without ever seeing plaintext
/// or key material.
/// </summary>
public sealed record EncryptedPayload(
    Guid KeyId,
    EncryptionAlgorithm Algorithm,
    byte[] CipherText,
    byte[] Nonce,
    byte[] AuthTag)
{
    public byte[] CipherText { get; init; } = CipherText.Length > 0
        ? CipherText
        : throw new ArgumentException("Ciphertext cannot be empty.", nameof(CipherText));

    public byte[] Nonce { get; init; } = Nonce.Length > 0
        ? Nonce
        : throw new ArgumentException("Nonce cannot be empty.", nameof(Nonce));
}
