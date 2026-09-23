namespace SecureApp.Presentation.Notifications;

/// <summary>
/// Bridges the widget's weekly-rozpis card tap (2026-09-22, user's own ask for a weekly schedule
/// section on the Phase 7 home-screen widget) into a plain Shell push navigation — <c>WorkplacePage</c>
/// isn't a tab (reached via <c>Routing.RegisterRoute</c>, pushed on top like <c>NotificationDetailPage</c>
/// already is), so this mirrors <see cref="NativeNotificationRouter"/>'s own COLD/WARM split exactly,
/// just carrying a route name instead of a notification id.
/// </summary>
public static class PendingWidgetRouteRouter
{
    private static string? _pendingRoute;

    public static event Action<string>? RouteRequested;

    /// <summary>Called by the platform (MainActivity) whenever the widget's Rozpis card launches or resumes the app — WARM (app already running, a listener is already subscribed from App.xaml.cs's own constructor) dispatches immediately; COLD stashes it for <see cref="ConsumePendingRoute"/>, same split as <see cref="NativeNotificationRouter"/>'s own remarks.</summary>
    public static void OnRouteRequested(string route)
    {
        if (RouteRequested is null)
            _pendingRoute = route;
        else
            RouteRequested.Invoke(route);
    }

    /// <summary>Called once, from App.xaml.cs's own Window.Created (the same earliest-safe-point the notification-detail cold case already relies on).</summary>
    public static string? ConsumePendingRoute()
    {
        var route = _pendingRoute;
        _pendingRoute = null;
        return route;
    }
}
