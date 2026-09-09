using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface ILogbookProcedureTypeRepository
{
    Task<LogbookProcedureType?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LogbookProcedureType>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(LogbookProcedureType type, CancellationToken ct = default);
    Task UpdateAsync(LogbookProcedureType type, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
