using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeNotificationService"/>
/// <remarks>
/// Windows implementation: deliberate no-op. The Notification Hub spec (NOTIFICATION_HUB_SPEC.md
/// §9/§24/§31) scopes this feature to Android specifically — Windows is this project's dev/test
/// target (see the original founding prompt), not a real deployment target for the notification
/// hub's OS-level surfacing. A genuine Windows toast implementation (Microsoft.Windows.AppNotifications)
/// is straightforward to add later if that ever changes; not built speculatively here.
/// </remarks>
public sealed class NativeNotificationService : INativeNotificationService
{
    public void ShowNotification(Guid notificationId, string title, string body, bool isImportant)
    {
        // No-op — see this class's own remarks.
    }

    public void CancelNotification(Guid notificationId)
    {
    }
}
