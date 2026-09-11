using SecureApp.Data.Persistence;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Data.Import;

/// <inheritdoc cref="IDocumentImportService"/>
public sealed class DocumentImportService : IDocumentImportService
{
    private readonly ICryptoService _crypto;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEncryptionKeyMetadataRepository _keyMetadataRepository;
    private readonly IAuditLogger _auditLogger;

    public DocumentImportService(
        ICryptoService crypto,
        IDocumentRepository documentRepository,
        IEncryptionKeyMetadataRepository keyMetadataRepository,
        IAuditLogger auditLogger)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _keyMetadataRepository = keyMetadataRepository ?? throw new ArgumentNullException(nameof(keyMetadataRepository));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    }

    public async Task<Document> ImportAsync(Stream fileStream, string fileName, Guid? folderId = null, Guid? sourceLibraryFileId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, ct);
        var plaintext = buffer.ToArray();

        var contentHash = await _crypto.ComputeHashAsync(plaintext, ct: ct);
        var encryptionKeyId = await GetOrCreateActiveEncryptionKeyIdAsync(ct);
        var encryptedContent = await _crypto.EncryptAsync(plaintext, encryptionKeyId, ct);

        var document = new Document(
            title: Path.GetFileNameWithoutExtension(fileName),
            fileName: fileName,
            documentType: ClassifyByExtension(fileName),
            originalSizeBytes: plaintext.LongLength,
            contentHash: contentHash,
            encryptedContent: encryptedContent,
            folderId: folderId,
            sourceLibraryFileId: sourceLibraryFileId);

        await _documentRepository.AddAsync(document, ct);
        await _auditLogger.LogAsync(AuditAction.DocumentImported, document.Id, fileName, ct);

        return document;
    }

    /// <summary>
    /// Reuses the vault's current active <see cref="KeyPurpose.DocumentEncryption"/> key,
    /// minting one on first use.
    /// </summary>
    private async Task<Guid> GetOrCreateActiveEncryptionKeyIdAsync(CancellationToken ct)
    {
        var activeMetadata = await _keyMetadataRepository.GetActiveKeyAsync(KeyPurpose.DocumentEncryption, ct);
        if (activeMetadata is not null)
            return activeMetadata.Id;

        var keyPair = await _crypto.GenerateEncryptionKeyPairAsync(ct);

        var metadata = new EncryptionKeyMetadata(keyPair.Algorithm, KeyPurpose.DocumentEncryption);
        // EncryptionKeyMetadata.Id is normally an Entity-assigned Guid.NewGuid() with no
        // relationship to any crypto key — its public ctor has no way to pin it to one. This
        // is the one place that correlation can be established: force metadata.Id to equal
        // the vault's KeyId (via the same reflection EntityMaterializer already uses for DB
        // hydration) so a later GetActiveKeyAsync(...).Id can be handed straight to
        // ICryptoService.EncryptAsync/DecryptAsync as a real, usable key id.
        EntityMaterializer.Set(metadata, nameof(Entity.Id), keyPair.KeyId);

        await _keyMetadataRepository.AddAsync(metadata, ct);
        await _auditLogger.LogAsync(AuditAction.EncryptionKeyGenerated, details: $"purpose={KeyPurpose.DocumentEncryption}", ct: ct);

        return metadata.Id;
    }

    private static DocumentType ClassifyByExtension(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => DocumentType.Pdf,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" => DocumentType.Image,
        ".xlsx" or ".xls" or ".csv" => DocumentType.Spreadsheet,
        ".txt" or ".md" or ".log" => DocumentType.PlainText,
        _ => DocumentType.Other
    };
}
