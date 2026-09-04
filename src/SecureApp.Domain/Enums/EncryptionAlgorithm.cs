namespace SecureApp.Domain.Enums;

/// <summary>
/// Algorithms available through <c>ICryptoService</c>. The "Hybrid" combination
/// pairs a post-quantum primitive with a classical one, so the scheme stays
/// safe even if only one of the two is ever broken.
/// </summary>
public enum EncryptionAlgorithm
{
    Unknown = 0,
    Aes256Gcm,
    MlKem768,                  // FIPS 203 — key encapsulation (PQC)
    MlDsa65,                   // FIPS 204 — digital signatures (PQC)
    HybridMlKem768Aes256Gcm
}
