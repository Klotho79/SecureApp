using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// Metadata about a cryptographic key managed by <c>ICryptoService</c>.
/// The key material itself is NEVER stored here — it lives only in the
/// platform's native secure storage (Android Keystore, iOS/macOS Keychain,
/// Windows DPAPI/Credential Locker) and is referenced solely by <see cref="Entity.Id"/>.
/// </summary>
public sealed class EncryptionKeyMetadata : Entity
{
    public EncryptionAlgorithm Algorithm { get; private set; }
    public KeyPurpose Purpose { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? RotatedAtUtc { get; private set; }

    private EncryptionKeyMetadata() { }

    public EncryptionKeyMetadata(EncryptionAlgorithm algorithm, KeyPurpose purpose)
    {
        Algorithm = algorithm;
        Purpose = purpose;
        IsActive = true;
    }

    public void Rotate()
    {
        IsActive = false;
        RotatedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }
}
