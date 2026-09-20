using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One day's work status (NOTIFICATION_HUB_SPEC.md §6/§7 — WORKPLACE and CALENDAR are the same
/// underlying schedule, just two views onto it: a "today + upcoming" list and a week strip).
/// Local per-device (like <see cref="Notification"/>), not relay-synced — this is one person's own
/// personal schedule, not shared/company-wide data (unlike the <see cref="Workplace"/> catalog it
/// optionally references).
///
/// <see cref="WorkplaceId"/>/<see cref="WorkplaceName"/> is a snapshot pair, not a live FK lookup —
/// same reasoning <c>GroupMember.DisplayName</c> already established: the catalog entry could be
/// renamed or deleted later, and an assignment should stay meaningful/displayable regardless.
/// </summary>
public sealed class WorkAssignment : Entity
{
    public DateOnly Date { get; private set; }
    public AssignmentType Type { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public Guid? WorkplaceId { get; private set; }
    public string? WorkplaceName { get; private set; }
    public string? Note { get; private set; }

    private WorkAssignment()
    {
        // Reserved for materialization by persistence infrastructure.
    }

    public WorkAssignment(
        DateOnly date,
        AssignmentType type,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        Guid? workplaceId = null,
        string? workplaceName = null,
        string? note = null)
    {
        Date = date;
        Type = type;
        StartTime = startTime;
        EndTime = endTime;
        WorkplaceId = workplaceId;
        WorkplaceName = string.IsNullOrWhiteSpace(workplaceName) ? null : workplaceName;
        Note = string.IsNullOrWhiteSpace(note) ? null : note;
    }

    public void Update(
        AssignmentType type,
        TimeOnly? startTime,
        TimeOnly? endTime,
        Guid? workplaceId,
        string? workplaceName,
        string? note)
    {
        Type = type;
        StartTime = startTime;
        EndTime = endTime;
        WorkplaceId = workplaceId;
        WorkplaceName = string.IsNullOrWhiteSpace(workplaceName) ? null : workplaceName;
        Note = string.IsNullOrWhiteSpace(note) ? null : note;
        Touch();
    }
}
