using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>Ingests a raw file stream: hashes, encrypts, and persists it as a <see cref="Document"/>.</summary>
public interface IDocumentImportService
{
    /// <summary><paramref name="sourceLibraryFileId"/> (2026-09-11) tags the imported document as a local copy of a shared-library file, so a later open can reuse it instead of re-downloading — see <see cref="Document.SourceLibraryFileId"/>.</summary>
    Task<Document> ImportAsync(Stream fileStream, string fileName, Guid? folderId = null, Guid? sourceLibraryFileId = null, CancellationToken ct = default);
}
