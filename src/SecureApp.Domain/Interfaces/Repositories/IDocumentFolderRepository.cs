using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IDocumentFolderRepository
{
    Task<DocumentFolder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentFolder>> GetChildrenAsync(Guid? parentFolderId, CancellationToken ct = default);
    Task AddAsync(DocumentFolder folder, CancellationToken ct = default);
    Task UpdateAsync(DocumentFolder folder, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
