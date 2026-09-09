using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface ILogbookChecklistRepository
{
    Task<LogbookChecklistTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LogbookChecklistTemplate>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(LogbookChecklistTemplate template, CancellationToken ct = default);
    Task UpdateAsync(LogbookChecklistTemplate template, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
