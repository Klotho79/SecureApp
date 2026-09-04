using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSession>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(ChatSession chatSession, CancellationToken ct = default);
    Task UpdateAsync(ChatSession chatSession, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
