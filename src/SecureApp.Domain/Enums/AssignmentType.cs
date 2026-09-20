namespace SecureApp.Domain.Enums;

/// <summary>Status/absence types for a <see cref="Entities.WorkAssignment"/> (NOTIFICATION_HUB_SPEC.md §6, §20).</summary>
public enum AssignmentType
{
    Work = 0,
    Vacation = 1,
    SickLeave = 2,
    BusinessTrip = 3,
    Training = 4,
    DayOff = 5,
    OnCall = 6,
    Other = 7
}
