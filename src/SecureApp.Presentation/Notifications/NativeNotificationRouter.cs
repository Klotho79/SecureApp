namespace SecureApp.Presentation.Notifications;

/// <summary>
/// Bridges a platform notification tap (spec §31 — "opening the app from an Android notification
/// should open that notification's own detail, not just the home screen") into a Shell navigation,
/// without <c>MainActivity</c> needing to know anything about Shell/routing itself. Two cases:
///
/// WARM (app already running, MainActivity.OnNewIntent fires): <see cref="NotificationTapped"/>
/// already has a listener (subscribed in <c>App.xaml.cs</c>'s constructor, which runs long before
/// any notification could be tapped) — <see cref="OnNotificationTapped"/> invokes it immediately.
///
/// COLD (app launched fresh from the tap, MainActivity.OnCreate fires before Shell/App even exist):
/// nothing is listening yet, so the id is stashed and picked up once by <c>App.xaml.cs</c>'s
/// <c>Window.Created</c> handler via <see cref="ConsumePendingNotificationId"/> — the same point
/// that's already the earliest safe place to touch <c>Shell.Current</c> (see that call site's own
/// remarks on why DLP setup waits for the same event).
/// </summary>
public static class NativeNotificationRouter
{
    private static Guid? _pendingNotificationId;

    public static event Action<Guid>? NotificationTapped;

    /// <summary>Called by the platform (MainActivity) whenever a notification launches or resumes the app.</summary>
    public static void OnNotificationTapped(Guid notificationId)
    {
        if (NotificationTapped is null)
            _pendingNotificationId = notificationId;
        else
            NotificationTapped.Invoke(notificationId);
    }

    /// <summary>Called once, after the app's Window/Shell is ready, to pick up a cold-start tap that happened before anything could listen.</summary>
    public static Guid? ConsumePendingNotificationId()
    {
        var id = _pendingNotificationId;
        _pendingNotificationId = null;
        return id;
    }
}
