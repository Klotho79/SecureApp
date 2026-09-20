using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IWorkAssignmentRepository
{
    Task<WorkAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ascending by date, inclusive on both ends — backs both the Week strip and the "next 7 days" upcoming list.</summary>
    Task<IReadOnlyList<WorkAssignment>> GetByDateRangeAsync(DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    Task AddAsync(WorkAssignment assignment, CancellationToken ct = default);
    Task UpdateAsync(WorkAssignment assignment, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
