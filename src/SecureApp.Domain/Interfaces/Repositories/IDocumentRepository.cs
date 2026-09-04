using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetByFolderAsync(Guid? folderId, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Document document, CancellationToken ct = default);
    Task UpdateAsync(Document document, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
