using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Entities;

/// <summary>
/// Metadata for a single stored document. The encrypted bytes live inside the
/// SQLCipher-protected local database (Data layer); this entity never holds
/// plaintext — only the ciphertext envelope produced by <c>ICryptoService</c>.
/// </summary>
public sealed class Document : Entity
{
    public string Title { get; private set; }
    public string FileName { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public long OriginalSizeBytes { get; private set; }
    public FileHash ContentHash { get; private set; }
    public EncryptedPayload EncryptedContent { get; private set; }
    public Guid? FolderId { get; private set; }
    public bool IsFavorite { get; private set; }
    public IReadOnlyList<string> Tags { get; private set; }

    private Document()
    {
        // Reserved for materialization by persistence/serialization infrastructure.
        Title = string.Empty;
        FileName = string.Empty;
        ContentHash = null!;
        EncryptedContent = null!;
        Tags = Array.Empty<string>();
    }

    public Document(
        string title,
        string fileName,
        DocumentType documentType,
        long originalSizeBytes,
        FileHash contentHash,
        EncryptedPayload encryptedContent,
        Guid? folderId = null,
        IReadOnlyList<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));
        if (originalSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(originalSizeBytes));

        Title = title;
        FileName = fileName;
        DocumentType = documentType;
        OriginalSizeBytes = originalSizeBytes;
        ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
        EncryptedContent = encryptedContent ?? throw new ArgumentNullException(nameof(encryptedContent));
        FolderId = folderId;
        Tags = tags ?? Array.Empty<string>();
    }

    public void Rename(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
            throw new ArgumentException("Title cannot be empty.", nameof(newTitle));

        Title = newTitle;
        Touch();
    }

    public void MoveTo(Guid? folderId)
    {
        FolderId = folderId;
        Touch();
    }

    public void ReplaceContent(EncryptedPayload newContent, FileHash newHash, long newSizeBytes)
    {
        EncryptedContent = newContent ?? throw new ArgumentNullException(nameof(newContent));
        ContentHash = newHash ?? throw new ArgumentNullException(nameof(newHash));
        OriginalSizeBytes = newSizeBytes;
        Touch();
    }

    public void SetTags(IReadOnlyList<string> tags)
    {
        Tags = tags ?? Array.Empty<string>();
        Touch();
    }

    public void ToggleFavorite(bool isFavorite)
    {
        IsFavorite = isFavorite;
        Touch();
    }
}
