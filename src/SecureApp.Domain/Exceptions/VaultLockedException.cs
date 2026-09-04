namespace SecureApp.Domain.Exceptions;

/// <summary>Thrown when an operation requiring an unlocked vault is attempted while it is locked.</summary>
public sealed class VaultLockedException : DomainException
{
    public VaultLockedException() : base("The vault is locked. Unlock it before performing this operation.") { }
}
