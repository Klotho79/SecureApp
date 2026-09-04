namespace SecureApp.Domain.Exceptions;

/// <summary>
/// Thrown when the Double Ratchet protocol fails (handshake, encrypt, decrypt, or skipped-key
/// lookup). Deliberately generic — same anti-oracle-leak convention as
/// <see cref="EncryptionOperationException"/>.
/// </summary>
public sealed class RatchetStateException : DomainException
{
    public RatchetStateException(string message) : base(message) { }
    public RatchetStateException(string message, Exception innerException) : base(message, innerException) { }
}
