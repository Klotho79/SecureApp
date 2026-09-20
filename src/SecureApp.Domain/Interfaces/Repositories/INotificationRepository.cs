using SecureApp.Domain.Entities;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Newest-first page matching <paramref name="filter"/> — see <see cref="NotificationFilter"/>'s
    /// own remarks for what each field narrows. Cursor-based on <paramref name="createdBeforeUtc"/>,
    /// not OFFSET (spec §19's performance requirement: stay responsive at 10,000+ archived rows) — an
    /// OFFSET page shifts under concurrently-arriving notifications and gets slower per page as the
    /// table grows; a "created before this timestamp" cursor does neither.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetPagedAsync(NotificationFilter filter, int take, DateTimeOffset? createdBeforeUtc = null, CancellationToken ct = default);

    Task<NotificationCounts> GetCountsAsync(CancellationToken ct = default);

    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task UpdateAsync(Notification notification, CancellationToken ct = default);
}
