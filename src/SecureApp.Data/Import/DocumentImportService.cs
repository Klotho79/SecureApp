using SecureApp.Data.Cryptography;
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
        var encryptionKeyId = await ActiveKeyHelper.GetOrCreateAsync(_keyMetadataRepository, _crypto, _auditLogger, KeyPurpose.DocumentEncryption, ct);
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

    private static DocumentType ClassifyByExtension(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => DocumentType.Pdf,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" => DocumentType.Image,
        ".xlsx" or ".xls" or ".csv" => DocumentType.Spreadsheet,
        ".txt" or ".md" or ".log" => DocumentType.PlainText,
        ".mp4" or ".mov" or ".m4v" or ".webm" or ".mkv" => DocumentType.Video,
        ".cmsdoc" => DocumentType.Cms,
        // .docx/.pptx (and anything else unrecognized) fall into Other — no in-app renderer for
        // them, the viewer offers "open externally" instead (2026-10-03 decision: a real in-app
        // Office preview needs a commercial rendering library, deferred for now).
        _ => DocumentType.Other
    };
}
