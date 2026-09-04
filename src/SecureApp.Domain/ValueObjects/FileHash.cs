namespace SecureApp.Domain.ValueObjects;

public enum HashAlgorithmKind
{
    Sha256,
    Sha3_256
}

/// <summary>Integrity fingerprint of a document's plaintext content, computed on import.</summary>
public sealed record FileHash(HashAlgorithmKind Algorithm, string HexValue)
{
    public string HexValue { get; init; } = !string.IsNullOrWhiteSpace(HexValue)
        ? HexValue
        : throw new ArgumentException("Hash value cannot be empty.", nameof(HexValue));
}
