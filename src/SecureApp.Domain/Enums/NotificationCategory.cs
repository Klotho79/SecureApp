namespace SecureApp.Domain.Enums;

/// <summary>
/// What generated a <see cref="Entities.Notification"/> — drives its grouping/icon in the
/// Notification Hub (see NOTIFICATION_HUB_SPEC.md). Deliberately SecureApp's own event sources
/// only for now (the user's own explicit scoping, 2026-09-20: build this extensibly but wire up
/// only the app's own events first, not a system-wide Android NotificationListenerService reading
/// every other app's notifications — that's a real permission/privacy escalation to revisit later,
/// not assumed here).
/// </summary>
public enum NotificationCategory
{
    Chat,
    Group,
    Library,
    Logbook,
    System,
    Other
}
