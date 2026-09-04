namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Authenticated-but-unencrypted per-message header the Double Ratchet protocol transmits
/// alongside each <see cref="EncryptedPayload"/> so the receiver can detect DH ratchet steps and
/// locate skipped message keys for out-of-order delivery.
/// </summary>
public sealed record RatchetMessageHeader(byte[] DhPublicKey, int PreviousChainLength, int MessageNumber)
{
    public byte[] DhPublicKey { get; init; } = DhPublicKey.Length > 0
        ? DhPublicKey
        : throw new ArgumentException("DH public key cannot be empty.", nameof(DhPublicKey));
}
