using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Public half + opaque identifier of an asymmetric key pair. The private key
/// itself never leaves secure/native storage (Android Keystore, iOS/macOS
/// Keychain, Windows DPAPI) — Domain and Data code only ever reference it by
/// <see cref="KeyId"/>, resolved through <c>ISecureVaultKeyStore</c>.
/// </summary>
public sealed record KeyPairReference(Guid KeyId, EncryptionAlgorithm Algorithm, byte[] PublicKey);
