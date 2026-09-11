using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Finds an already-imported local copy of a shared-library file, if one exists (2026-09-11) — lets an attachment open reuse the local copy instead of re-downloading it from the relay. See <see cref="Document.SourceLibraryFileId"/>.</summary>
    Task<Document?> GetBySourceLibraryFileIdAsync(Guid libraryFileId, CancellationToken ct = default);

    Task<IReadOnlyList<Document>> GetByFolderAsync(Guid? folderId, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Document document, CancellationToken ct = default);
    Task UpdateAsync(Document document, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
