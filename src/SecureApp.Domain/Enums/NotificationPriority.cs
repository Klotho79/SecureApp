namespace SecureApp.Domain.Enums;

/// <summary>
/// Ordered so a plain numeric comparison means what it looks like — <c>Priority &gt;=
/// NotificationPriority.Important</c> is exactly "Important or more urgent", the check the
/// IMPORTANT section (see NOTIFICATION_HUB_SPEC.md §4/§17) is built on. Four levels, matching the
/// spec's own cap ("use a maximum of approximately 3–4 priority levels").
/// </summary>
public enum NotificationPriority
{
    Informational = 0,
    Normal = 1,
    Important = 2,
    Critical = 3
}
