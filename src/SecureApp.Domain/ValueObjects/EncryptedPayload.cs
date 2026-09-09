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
    /// <summary>
    /// Deliberately NOT length-checked (2026-09-09 — a real bug caught live: sending a group/1:1
    /// message that's an attachment with no text body encrypts a zero-length plaintext, and AES-GCM's
    /// ciphertext is exactly as long as its plaintext — an empty ciphertext here is the CORRECT,
    /// legitimate encoding of an empty message, authenticated by <see cref="AuthTag"/> just like any
    /// other, not a sign anything went wrong. The old `CipherText.Length > 0` check rejected every
    /// such attachment-only send outright with "Ciphertext cannot be empty." <see cref="Nonce"/> stays
    /// validated below — that one must never legitimately be empty, unlike this.
    /// </summary>
    public byte[] CipherText { get; init; } = CipherText;

    public byte[] Nonce { get; init; } = Nonce.Length > 0
        ? Nonce
        : throw new ArgumentException("Nonce cannot be empty.", nameof(Nonce));
}
