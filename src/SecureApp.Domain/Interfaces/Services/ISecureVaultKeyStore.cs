namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Abstraction over the platform's native secure storage (Android Keystore,
/// iOS/macOS Keychain, Windows DPAPI/Credential Locker) used to hold private
/// key material and the vault's master-passphrase-derived key. Implemented in
/// the Presentation layer, where platform-specific secure storage APIs are
/// actually reachable — the Data layer stays platform-agnostic.
/// </summary>
public interface ISecureVaultKeyStore
{
    Task StoreSecretAsync(string key, ReadOnlyMemory<byte> secret, CancellationToken ct = default);
    Task<byte[]?> RetrieveSecretAsync(string key, CancellationToken ct = default);
    Task RemoveSecretAsync(string key, CancellationToken ct = default);
}
