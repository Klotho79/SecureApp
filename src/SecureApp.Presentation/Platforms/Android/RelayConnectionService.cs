using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Platforms.Android;

/// <summary>
/// Keeps the app process alive so messages and notifications keep arriving while SecureApp is closed
/// (2026-09-26, user's ask). The relay connection itself is still App's own supervisor loop — this
/// service only holds a foreground slot so Android doesn't freeze or kill the process. Type
/// "remoteMessaging" (Android 14+) is the one meant for a chat connection and, unlike "dataSync",
/// has no daily time limit. Android requires the small ongoing notification that comes with it.
/// Started from MainActivity and again after a reboot / app update (<see cref="RelayConnectionBootReceiver"/>).
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeRemoteMessaging)]
public sealed class RelayConnectionService : Service
{
    private const string ChannelId = "secureapp.connection";
    private const int NotificationId = 7101;

    public static void Start(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(RelayConnectionService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(RelayConnectionService), "starting the background connection service failed", ex);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            EnsureChannel();
            var notification = BuildNotification();
            if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
                StartForeground(NotificationId, notification, ForegroundService.TypeRemoteMessaging);
            else
                StartForeground(NotificationId, notification);
            AppLog.Event("bg-service.started");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(RelayConnectionService), "entering the foreground failed", ex);
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        return StartCommandResult.Sticky;
    }

    private Notification BuildNotification()
    {
        var open = new Intent(this, typeof(MainActivity));
        open.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pending = PendingIntent.GetActivity(this, NotificationId, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("SecureApp je připojen")
            .SetContentText("Přijímá zprávy i když je aplikace zavřená.")
            .SetSmallIcon(ApplicationInfo!.Icon)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityMin)
            .SetCategory(NotificationCompat.CategoryService)
            .SetContentIntent(pending)
            .Build();
    }

    private void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        if (GetSystemService(NotificationService) is not NotificationManager manager) return;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Připojení na pozadí", NotificationImportance.Min)
        {
            Description = "Trvalé upozornění, že SecureApp přijímá zprávy na pozadí. Lze ho skrýt v nastavení oznámení.",
        });
    }
}

/// <summary>Restarts <see cref="RelayConnectionService"/> after a reboot or an app update, so messages arrive without opening the app first.</summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class RelayConnectionBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        if (intent?.Action is Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced)
            RelayConnectionService.Start(context);
    }
}
