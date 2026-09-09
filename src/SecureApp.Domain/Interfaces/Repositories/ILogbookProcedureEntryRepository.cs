using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface ILogbookProcedureEntryRepository
{
    Task<IReadOnlyList<LogbookProcedureEntry>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(LogbookProcedureEntry entry, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
