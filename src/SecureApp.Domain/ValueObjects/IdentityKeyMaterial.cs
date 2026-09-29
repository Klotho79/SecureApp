namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Raw chat-identity key material (2026-09-29, disaster-recovery feature — see IIdentityBackupService's
/// own remarks) — the ONLY place in the whole codebase these three secrets are ever allowed to travel
/// together as plain bytes outside the vault. Exists solely so a device that loses its local data can
/// restore the SAME identity (same public key, so existing peers still recognize/trust it and the
/// app's own resync machinery can reconnect existing sessions) instead of minting a brand-new one.
/// <see cref="AesKey"/> is the ML-KEM self-encapsulated shared secret <c>BouncyCastleCryptoService</c>
/// derives once at generation time and reuses for every document/message encrypted under this KeyId —
/// without it, the private key alone isn't enough to decrypt anything already encrypted under this key.
/// </summary>
public sealed record IdentityKeyMaterial(Guid KeyId, byte[] PrivateKey, byte[] PublicKey, byte[] AesKey);
