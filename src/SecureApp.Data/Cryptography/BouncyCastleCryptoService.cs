using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Data.Cryptography;

/// <summary>
/// <see cref="ICryptoService"/> implemented with BouncyCastle. Key material never
/// leaves this class as plaintext beyond the round-trip through <see cref="ISecureVaultKeyStore"/>
/// (native secure storage, wired in from the Presentation layer) — callers only ever see
/// <see cref="KeyPairReference"/> (public half only) and <see cref="EncryptedPayload"/>.
///
/// Encryption keys: an ML-KEM-768 (FIPS 203) pair is generated and immediately
/// self-encapsulated — this vault is both the "sender" and "recipient", so there is no
/// second party to exchange with; encapsulating against our own freshly-generated public
/// key is simply how a quantum-resistant 256-bit secret gets derived and bound to the
/// keypair. That secret becomes the AES-256-GCM key used for every document encrypted
/// under this <c>KeyId</c> — see <see cref="EncryptionAlgorithm.HybridMlKem768Aes256Gcm"/>.
///
/// Signing keys: a plain ML-DSA-65 (FIPS 204) pair — no hybrid step needed since
/// signatures aren't a secrecy primitive.
/// </summary>
public sealed class BouncyCastleCryptoService : ICryptoService
{
    private const int AesKeySizeBytes = 32; // 256-bit
    private const int GcmNonceSizeBytes = 12; // 96-bit, standard/recommended for GCM
    private const int GcmMacSizeBits = 128;
    private const int GcmMacSizeBytes = GcmMacSizeBits / 8;

    private static readonly MLKemParameters KemParameters = MLKemParameters.ml_kem_768;
    private static readonly MLDsaParameters DsaParameters = MLDsaParameters.ml_dsa_65;

    private readonly ISecureVaultKeyStore _vault;
    private readonly SecureRandom _random = new();

    public BouncyCastleCryptoService(ISecureVaultKeyStore vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<KeyPairReference> GenerateEncryptionKeyPairAsync(CancellationToken ct = default)
    {
        var kpg = new MLKemKeyPairGenerator();
        kpg.Init(new MLKemKeyGenerationParameters(_random, KemParameters));
        var keyPair = kpg.GenerateKeyPair();

        var publicKey = (MLKemPublicKeyParameters)keyPair.Public;
        var privateKey = ((MLKemPrivateKeyParameters)keyPair.Private)
            .WithPreferredFormat(MLKemPrivateKeyParameters.Format.EncodingOnly);

        var encapsulator = new MLKemEncapsulator(KemParameters);
        encapsulator.Init(publicKey);
        var kemCipherText = new byte[encapsulator.EncapsulationLength];
        var sharedSecret = new byte[encapsulator.SecretLength];
        encapsulator.Encapsulate(kemCipherText, 0, kemCipherText.Length, sharedSecret, 0, sharedSecret.Length);

        if (sharedSecret.Length != AesKeySizeBytes)
        {
            // ML-KEM's shared secret is defined as 32 bytes for every parameter set (FIPS 203),
            // so this can only mean a future BouncyCastle version changed that contract.
            throw new EncryptionOperationException("Key generation failed.");
        }

        var keyId = Guid.NewGuid();
        await _vault.StoreSecretAsync(VaultKey(keyId, "kem-priv"), privateKey.GetEncoded(), ct);
        await _vault.StoreSecretAsync(VaultKey(keyId, "kem-pub"), publicKey.GetEncoded(), ct);
        await _vault.StoreSecretAsync(VaultKey(keyId, "aes-key"), sharedSecret, ct);

        return new KeyPairReference(keyId, EncryptionAlgorithm.HybridMlKem768Aes256Gcm, publicKey.GetEncoded());
    }

    public async Task<byte[]> GetEncryptionPublicKeyAsync(Guid keyId, CancellationToken ct = default)
        => await _vault.RetrieveSecretAsync(VaultKey(keyId, "kem-pub"), ct) ?? throw new VaultLockedException();

    public async Task<KeyPairReference> GenerateSigningKeyPairAsync(CancellationToken ct = default)
    {
        var kpg = new MLDsaKeyPairGenerator();
        kpg.Init(new MLDsaKeyGenerationParameters(_random, DsaParameters));
        var keyPair = kpg.GenerateKeyPair();

        var publicKey = (MLDsaPublicKeyParameters)keyPair.Public;
        var privateKey = ((MLDsaPrivateKeyParameters)keyPair.Private)
            .WithPreferredFormat(MLDsaPrivateKeyParameters.Format.EncodingOnly);

        var keyId = Guid.NewGuid();
        await _vault.StoreSecretAsync(VaultKey(keyId, "dsa-priv"), privateKey.GetEncoded(), ct);
        await _vault.StoreSecretAsync(VaultKey(keyId, "dsa-pub"), publicKey.GetEncoded(), ct);

        return new KeyPairReference(keyId, EncryptionAlgorithm.MlDsa65, publicKey.GetEncoded());
    }

    public async Task<EncryptedPayload> EncryptAsync(ReadOnlyMemory<byte> plaintext, Guid keyId, CancellationToken ct = default)
    {
        var aesKey = await _vault.RetrieveSecretAsync(VaultKey(keyId, "aes-key"), ct)
            ?? throw new VaultLockedException();

        var nonce = new byte[GcmNonceSizeBytes];
        _random.NextBytes(nonce);

        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(aesKey), GcmMacSizeBits, nonce));

        var plaintextBytes = plaintext.ToArray();
        var buffer = new byte[cipher.GetOutputSize(plaintextBytes.Length)];
        int len = cipher.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, buffer, 0);
        len += cipher.DoFinal(buffer, len);

        // GcmBlockCipher appends the auth tag to the end of the output; split it back out
        // so it round-trips through EncryptedPayload's dedicated AuthTag field.
        var cipherText = buffer[..(len - GcmMacSizeBytes)];
        var authTag = buffer[(len - GcmMacSizeBytes)..len];

        return new EncryptedPayload(keyId, EncryptionAlgorithm.HybridMlKem768Aes256Gcm, cipherText, nonce, authTag);
    }

    public async Task<byte[]> DecryptAsync(EncryptedPayload payload, CancellationToken ct = default)
    {
        var aesKey = await _vault.RetrieveSecretAsync(VaultKey(payload.KeyId, "aes-key"), ct)
            ?? throw new VaultLockedException();

        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(false, new AeadParameters(new KeyParameter(aesKey), GcmMacSizeBits, payload.Nonce));

        var combined = new byte[payload.CipherText.Length + payload.AuthTag.Length];
        payload.CipherText.CopyTo(combined, 0);
        payload.AuthTag.CopyTo(combined, payload.CipherText.Length);

        var buffer = new byte[cipher.GetOutputSize(combined.Length)];
        int len = cipher.ProcessBytes(combined, 0, combined.Length, buffer, 0);
        try
        {
            len += cipher.DoFinal(buffer, len);
        }
        catch (InvalidCipherTextException ex)
        {
            // Deliberately generic message — see the class-level warning on EncryptionOperationException.
            throw new EncryptionOperationException("Decryption failed.", ex);
        }

        return len == buffer.Length ? buffer : buffer[..len];
    }

    public async Task<byte[]> SignAsync(ReadOnlyMemory<byte> data, Guid signingKeyId, CancellationToken ct = default)
    {
        var encodedPrivateKey = await _vault.RetrieveSecretAsync(VaultKey(signingKeyId, "dsa-priv"), ct)
            ?? throw new VaultLockedException();

        var privateKey = MLDsaPrivateKeyParameters.FromEncoding(DsaParameters, encodedPrivateKey);

        var signer = new MLDsaSigner(DsaParameters, deterministic: true);
        signer.Init(true, privateKey);
        var dataBytes = data.ToArray();
        signer.BlockUpdate(dataBytes, 0, dataBytes.Length);
        return signer.GenerateSignature();
    }

    public async Task<bool> VerifyAsync(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, Guid signingKeyId, CancellationToken ct = default)
    {
        var encodedPublicKey = await _vault.RetrieveSecretAsync(VaultKey(signingKeyId, "dsa-pub"), ct)
            ?? throw new VaultLockedException();

        var publicKey = MLDsaPublicKeyParameters.FromEncoding(DsaParameters, encodedPublicKey);

        var signer = new MLDsaSigner(DsaParameters, deterministic: true);
        signer.Init(false, publicKey);
        var dataBytes = data.ToArray();
        signer.BlockUpdate(dataBytes, 0, dataBytes.Length);
        return signer.VerifySignature(signature.ToArray());
    }

    public Task<FileHash> ComputeHashAsync(ReadOnlyMemory<byte> data, HashAlgorithmKind algorithm = HashAlgorithmKind.Sha256, CancellationToken ct = default)
    {
        IDigest digest = algorithm switch
        {
            HashAlgorithmKind.Sha256 => new Sha256Digest(),
            HashAlgorithmKind.Sha3_256 => new Sha3Digest(256),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported hash algorithm.")
        };

        var dataBytes = data.ToArray();
        digest.BlockUpdate(dataBytes, 0, dataBytes.Length);
        var output = new byte[digest.GetDigestSize()];
        digest.DoFinal(output, 0);

        return Task.FromResult(new FileHash(algorithm, Convert.ToHexStringLower(output)));
    }

    public Task<(byte[] KemCipherText, byte[] SharedSecret)> EncapsulateAsync(byte[] peerPublicKey, CancellationToken ct = default)
    {
        var publicKey = MLKemPublicKeyParameters.FromEncoding(KemParameters, peerPublicKey);

        var encapsulator = new MLKemEncapsulator(KemParameters);
        encapsulator.Init(publicKey);
        var kemCipherText = new byte[encapsulator.EncapsulationLength];
        var sharedSecret = new byte[encapsulator.SecretLength];
        encapsulator.Encapsulate(kemCipherText, 0, kemCipherText.Length, sharedSecret, 0, sharedSecret.Length);

        return Task.FromResult((kemCipherText, sharedSecret));
    }

    public async Task<byte[]> DecapsulateAsync(Guid ownKeyId, byte[] kemCipherText, CancellationToken ct = default)
    {
        var encodedPrivateKey = await _vault.RetrieveSecretAsync(VaultKey(ownKeyId, "kem-priv"), ct)
            ?? throw new VaultLockedException();

        var privateKey = MLKemPrivateKeyParameters.FromEncoding(KemParameters, encodedPrivateKey);

        var decapsulator = new MLKemDecapsulator(KemParameters);
        decapsulator.Init(privateKey);
        var sharedSecret = new byte[decapsulator.SecretLength];
        decapsulator.Decapsulate(kemCipherText, 0, kemCipherText.Length, sharedSecret, 0, sharedSecret.Length);

        return sharedSecret;
    }

    public Task<byte[]> DeriveKeyAsync(byte[] inputKeyMaterial, byte[]? salt, string info, int outputLength, CancellationToken ct = default)
    {
        var generator = new HkdfBytesGenerator(new Sha256Digest());
        generator.Init(new HkdfParameters(inputKeyMaterial, salt, System.Text.Encoding.UTF8.GetBytes(info)));

        var output = new byte[outputLength];
        generator.GenerateBytes(output, 0, output.Length);
        return Task.FromResult(output);
    }

    public Task<EncryptedPayload> EncryptWithKeyAsync(ReadOnlyMemory<byte> plaintext, Guid correlationId, byte[] key, CancellationToken ct = default)
    {
        var nonce = new byte[GcmNonceSizeBytes];
        _random.NextBytes(nonce);

        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), GcmMacSizeBits, nonce));

        var plaintextBytes = plaintext.ToArray();
        var buffer = new byte[cipher.GetOutputSize(plaintextBytes.Length)];
        int len = cipher.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, buffer, 0);
        len += cipher.DoFinal(buffer, len);

        var cipherText = buffer[..(len - GcmMacSizeBytes)];
        var authTag = buffer[(len - GcmMacSizeBytes)..len];

        return Task.FromResult(new EncryptedPayload(correlationId, EncryptionAlgorithm.Aes256Gcm, cipherText, nonce, authTag));
    }

    public Task<byte[]> DecryptWithKeyAsync(EncryptedPayload payload, byte[] key, CancellationToken ct = default)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(false, new AeadParameters(new KeyParameter(key), GcmMacSizeBits, payload.Nonce));

        var combined = new byte[payload.CipherText.Length + payload.AuthTag.Length];
        payload.CipherText.CopyTo(combined, 0);
        payload.AuthTag.CopyTo(combined, payload.CipherText.Length);

        var buffer = new byte[cipher.GetOutputSize(combined.Length)];
        int len = cipher.ProcessBytes(combined, 0, combined.Length, buffer, 0);
        try
        {
            len += cipher.DoFinal(buffer, len);
        }
        catch (InvalidCipherTextException ex)
        {
            // Deliberately generic message — see the class-level warning on EncryptionOperationException.
            throw new EncryptionOperationException("Decryption failed.", ex);
        }

        return Task.FromResult(len == buffer.Length ? buffer : buffer[..len]);
    }

    /// <summary>Namespaces a logical secret under a key pair's id so the flat <see cref="ISecureVaultKeyStore"/> string keyspace never collides.</summary>
    private static string VaultKey(Guid keyId, string label) => $"cryptokey:{keyId:N}:{label}";
}
