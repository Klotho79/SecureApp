using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeNotificationService"/>
/// <remarks>
/// Android implementation: two notification channels (Important/Normal — spec §17's own "a new
/// important notification should be visually more prominent"), a tap PendingIntent that relaunches
/// <see cref="MainActivity"/> with the notification's id as an extra (picked up via
/// <see cref="Notifications.NativeNotificationRouter"/>), delivered through
/// <see cref="NotificationManagerCompat"/> so a missing POST_NOTIFICATIONS grant (Android 13+, not
/// yet accepted by the user) is a silent no-op rather than a crash — same "best-effort, never take
/// down the caller" policy this app already applies to every background notification path.
/// </remarks>
public sealed class NativeNotificationService : INativeNotificationService
{
    private const string ImportantChannelId = "secureapp.important";
    private const string NormalChannelId = "secureapp.normal";
    private static bool _channelsEnsured;

    public void ShowNotification(Guid notificationId, string title, string body, bool isImportant)
    {
        var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
        if (context?.ApplicationInfo is null) return;

        try
        {
            EnsureChannels(context);

            var intent = new Intent(context, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            intent.PutExtra("notificationId", notificationId.ToString());
            var pendingIntent = PendingIntent.GetActivity(
                context, notificationId.GetHashCode(), intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var channelId = isImportant ? ImportantChannelId : NormalChannelId;
            var builder = new NotificationCompat.Builder(context, channelId)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetSmallIcon(context.ApplicationInfo.Icon)
                .SetPriority(isImportant ? NotificationCompat.PriorityHigh : NotificationCompat.PriorityDefault)
                .SetAutoCancel(true)
                .SetContentIntent(pendingIntent);

            NotificationManagerCompat.From(context).Notify(notificationId.GetHashCode(), builder.Build());
        }
        catch
        {
            // Best-effort — see this class's own remarks. Missing runtime permission, a
            // Notify-throws-SecurityException edge case on some OEM skins, etc. must never take
            // down the caller (NotificationPublisher's own already-persisted Notification row is
            // what actually matters; this is a secondary OS-level surfacing of it).
        }
    }

    public void CancelNotification(Guid notificationId)
    {
        try
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            if (context is null) return;
            NotificationManagerCompat.From(context).Cancel(notificationId.GetHashCode());
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void EnsureChannels(Context context)
    {
        if (_channelsEnsured) return;
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            _channelsEnsured = true;
            return; // Notification channels don't exist before Android 8 (API 26).
        }

        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            return;

        manager.CreateNotificationChannel(new NotificationChannel(ImportantChannelId, "Důležitá oznámení", global::Android.App.NotificationImportance.High)
        {
            Description = "Kritická a důležitá oznámení SecureApp"
        });
        manager.CreateNotificationChannel(new NotificationChannel(NormalChannelId, "Oznámení", global::Android.App.NotificationImportance.Default)
        {
            Description = "Běžná oznámení SecureApp"
        });
        _channelsEnsured = true;
    }
}
