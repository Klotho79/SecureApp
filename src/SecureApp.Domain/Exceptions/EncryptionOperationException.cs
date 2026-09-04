namespace SecureApp.Domain.Exceptions;

/// <summary>
/// Thrown when a cryptographic operation (encrypt/decrypt/sign/verify/key
/// generation) fails. Deliberately generic — the message must never leak
/// details (e.g. "MAC mismatch" vs "bad key") that could aid a padding-oracle
/// or timing-based attack against the vault.
/// </summary>
public sealed class EncryptionOperationException : DomainException
{
    public EncryptionOperationException(string message) : base(message) { }
    public EncryptionOperationException(string message, Exception innerException) : base(message, innerException) { }
}
