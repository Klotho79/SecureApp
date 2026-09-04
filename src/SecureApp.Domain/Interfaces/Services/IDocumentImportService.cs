using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>Ingests a raw file stream: hashes, encrypts, and persists it as a <see cref="Document"/>.</summary>
public interface IDocumentImportService
{
    Task<Document> ImportAsync(Stream fileStream, string fileName, Guid? folderId = null, CancellationToken ct = default);
}
