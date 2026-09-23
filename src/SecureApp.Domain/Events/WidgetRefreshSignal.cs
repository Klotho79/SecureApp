namespace SecureApp.Domain.Events;

/// <summary>
/// Fires whenever data the Android home-screen widget shows (Phase 7, NOTIFICATION_HUB_SPEC.md §9)
/// changes — both <c>NotificationRepository</c> (create/read/archive/etc. a <see cref="Entities.Notification"/>)
/// and <c>WorkAssignmentRepository</c> (add/update/delete a <see cref="Entities.WorkAssignment"/>, the
/// weekly rozpis the widget also shows — 2026-09-22, user's own ask) raise it after a successful write,
/// and the widget subscribes once at startup to refresh itself immediately rather than waiting on
/// Android's own ~30-minute periodic-update floor. Deliberately a single shared signal rather than one
/// per entity type — the widget always redraws its whole self on any change anyway (no partial-update
/// path), so there is nothing for two separate events to buy here, only two subscriptions to keep in
/// sync. Plain Domain-layer static event, not a platform service — see this file's own name history
/// (was <c>NotificationsChangedSignal</c>, renamed once WorkAssignment started raising it too) for why
/// Data can raise it with zero Android reference at all (Clean Architecture — Data must not depend on
/// Presentation/Platforms).
/// </summary>
public static class WidgetRefreshSignal
{
    public static event Action? Changed;

    public static void Raise() => Changed?.Invoke();
}
