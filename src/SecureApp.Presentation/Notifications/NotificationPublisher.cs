using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Notifications;

/// <summary>
/// Turns SecureApp's own existing events into Notification Hub rows (NOTIFICATION_HUB_SPEC.md) —
/// called from <c>App.xaml.cs</c>'s already-centralized, always-on event handlers
/// (<c>OnEnvelopeReceived</c>/<c>OnGroupInviteReceived</c>/<c>OnPairingInviteReceived</c>), the same
/// places that already persist the underlying event regardless of which screen (if any) is open —
/// so a Notification is created the same way a message is: unconditionally, not just while the
/// Notification Hub page happens to be on screen. Best-effort by design (same policy as every other
/// background-sweep helper in this app) — a failure here must never take down the message/pairing
/// handling it's attached to.
///
/// <paramref name="nativeNotificationService"/> across every method below is optional (2026-09-20,
/// Phase 8) — passing it also posts a real OS notification (<see cref="INativeNotificationService"/>)
/// alongside the always-persisted Notification Hub row; omitting it just skips that OS-level
/// surfacing, for any call site that doesn't have one handy or doesn't want it.
///
/// Known limitation, not fixed here: <c>IMessagingService.ReceiveMessageAsync</c>'s idempotency
/// guard can return an already-stored row for a genuine wire-level duplicate delivery, and this
/// class has no reliable way to tell that apart from a first delivery — a rare redelivery could
/// occasionally create a second Notification for the same message. Low-impact (mark-read/archive
/// stay correct either way) and not worth the added complexity to close in this first pass.
/// </summary>
public static class NotificationPublisher
{
    private const int PreviewMaxLength = 140;

    public static Task PublishDirectMessageAsync(
        INotificationRepository notificationRepository,
        string peerDisplayName,
        string plaintextPreview,
        Guid chatSessionId,
        INativeNotificationService? nativeNotificationService = null,
        CancellationToken ct = default)
    {
        var notification = new Notification(
            title: peerDisplayName,
            body: Truncate(plaintextPreview),
            category: NotificationCategory.Chat,
            priority: NotificationPriority.Normal,
            relatedChatSessionId: chatSessionId);
        return TryAddAsync(notificationRepository, notification, nativeNotificationService, ct);
    }

    public static Task PublishGroupMessageAsync(
        INotificationRepository notificationRepository,
        string groupName,
        string senderDisplayName,
        string plaintextPreview,
        Guid chatSessionId,
        Guid groupChatId,
        INativeNotificationService? nativeNotificationService = null,
        CancellationToken ct = default)
    {
        var notification = new Notification(
            title: groupName,
            body: $"{senderDisplayName}: {Truncate(plaintextPreview)}",
            category: NotificationCategory.Group,
            priority: NotificationPriority.Normal,
            relatedChatSessionId: chatSessionId,
            relatedGroupChatId: groupChatId);
        return TryAddAsync(notificationRepository, notification, nativeNotificationService, ct);
    }

    /// <summary>A brand new 1:1 pairing/resync completing (spec §25 — "who I need to contact" territory) — informational, not urgent, so <see cref="NotificationPriority.Informational"/> rather than Normal.</summary>
    public static Task PublishPairingCompletedAsync(
        INotificationRepository notificationRepository,
        string peerDisplayName,
        Guid chatSessionId,
        INativeNotificationService? nativeNotificationService = null,
        CancellationToken ct = default)
    {
        var notification = new Notification(
            title: "Nové spojení",
            body: $"Jste nyní spárováni s {peerDisplayName}.",
            category: NotificationCategory.Chat,
            priority: NotificationPriority.Informational,
            relatedChatSessionId: chatSessionId);
        return TryAddAsync(notificationRepository, notification, nativeNotificationService, ct);
    }

    public static Task PublishGroupInviteAsync(
        INotificationRepository notificationRepository,
        string groupName,
        Guid groupChatId,
        INativeNotificationService? nativeNotificationService = null,
        CancellationToken ct = default)
    {
        var notification = new Notification(
            title: "Přidání do skupiny",
            body: $"Byli jste přidáni do skupiny „{groupName}“.",
            category: NotificationCategory.Group,
            priority: NotificationPriority.Normal,
            relatedGroupChatId: groupChatId);
        return TryAddAsync(notificationRepository, notification, nativeNotificationService, ct);
    }

    public static Task PublishLibraryFileAddedAsync(
        INotificationRepository notificationRepository,
        string fileName,
        Guid libraryFileId,
        INativeNotificationService? nativeNotificationService = null,
        CancellationToken ct = default)
    {
        var notification = new Notification(
            title: "Nový soubor ve sdílené knihovně",
            body: fileName,
            category: NotificationCategory.Library,
            priority: NotificationPriority.Normal,
            relatedLibraryFileId: libraryFileId);
        return TryAddAsync(notificationRepository, notification, nativeNotificationService, ct);
    }

    /// <summary>A genuine system-level problem (e.g. an auto-heal failure — see <c>App.xaml.cs</c>'s own diagnostics-reporter call sites) surfaced into the same feed the user already checks for everything else, instead of only living in the separate Settings diagnostics log. <see cref="NotificationPriority.Important"/> — system errors belong in the IMPORTANT section, not buried among ordinary chat traffic.</summary>
    public static Task PublishSystemWarningAsync(
        INotificationRepository notificationRepository,
        string title,
        string body,
        INativeNotificationService? nativeNotificationService = null,
        CancellationToken ct = default)
    {
        // Normal, not Important (2026-09-21, user's own ask) — system notifications (sync results,
        // relay events, ...) are informational and must never count toward the Důležité badge; see
        // NotificationRepository's own remarks for the matching query-level exclusion (defense in
        // depth against a future is_manually_important flip still counting one).
        var notification = new Notification(
            title: title,
            body: body,
            category: NotificationCategory.System,
            priority: NotificationPriority.Normal);
        return TryAddAsync(notificationRepository, notification, nativeNotificationService, ct);
    }

    private static async Task TryAddAsync(INotificationRepository notificationRepository, Notification notification, INativeNotificationService? nativeNotificationService, CancellationToken ct)
    {
        try
        {
            await notificationRepository.AddAsync(notification, ct);
        }
        catch
        {
            // Best-effort — see class-level remarks.
            return;
        }

        try
        {
            nativeNotificationService?.ShowNotification(notification.Id, notification.Title, notification.Body, notification.IsImportant);
        }
        catch
        {
            // Best-effort — the Notification Hub row above is already safely persisted either way.
        }
    }

    private static string Truncate(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var singleLine = text.ReplaceLineEndings(" ");
        return singleLine.Length <= PreviewMaxLength ? singleLine : singleLine[..PreviewMaxLength] + "…";
    }
}
