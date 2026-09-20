namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Posts a real OS-level notification for a Notification Hub row (NOTIFICATION_HUB_SPEC.md §10/§24) —
/// separate from the Notification Hub's own in-app feed, which is always updated regardless of
/// whether this succeeds. Each platform implements this in its own
/// <c>Platforms/&lt;Platform&gt;/NativeNotificationService.cs</c> (same type name/namespace in each
/// folder — only the one matching the active target framework is compiled in, the same pattern
/// <c>INativeDlpService</c> already established).
/// </summary>
public interface INativeNotificationService
{
    /// <summary>Best-effort — never throws. A platform with no meaningful notification mechanism, or missing runtime permission, silently no-ops.</summary>
    void ShowNotification(Guid notificationId, string title, string body, bool isImportant);
}
