using SecureApp.Data.Persistence;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Data.Cryptography;

/// <summary>
/// The shared "reuse the vault's active key for this purpose, mint one on first use" pattern
/// (message storage, chat identity, document encryption). <see cref="EncryptionKeyMetadata"/>'s Id is
/// normally an Entity-assigned Guid.NewGuid() with no relationship to any crypto key and its public ctor
/// can't pin it to one — so the one place that correlation is established is here: metadata.Id is forced
/// to equal the vault's KeyId (same reflection EntityMaterializer uses for DB hydration), letting a later
/// GetActiveKeyAsync(...).Id be handed straight to ICryptoService.EncryptAsync/DecryptAsync.
/// </summary>
internal static class ActiveKeyHelper
{
    public static async Task<Guid> GetOrCreateAsync(
        IEncryptionKeyMetadataRepository keyMetadataRepository,
        ICryptoService crypto,
        IAuditLogger auditLogger,
        KeyPurpose purpose,
        CancellationToken ct)
    {
        var activeMetadata = await keyMetadataRepository.GetActiveKeyAsync(purpose, ct);
        if (activeMetadata is not null)
            return activeMetadata.Id;

        var keyPair = await crypto.GenerateEncryptionKeyPairAsync(ct);

        var metadata = new EncryptionKeyMetadata(keyPair.Algorithm, purpose);
        EntityMaterializer.Set(metadata, nameof(Entity.Id), keyPair.KeyId);

        await keyMetadataRepository.AddAsync(metadata, ct);
        await auditLogger.LogAsync(AuditAction.EncryptionKeyGenerated, details: $"purpose={purpose}", ct: ct);

        return metadata.Id;
    }
}
