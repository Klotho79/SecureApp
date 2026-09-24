using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using SecureApp.Domain.Interfaces.Services;
using Debug = System.Diagnostics.Debug;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Foreground <c>Service</c> that downloads an update APK independently of any page or ViewModel
/// (2026-09-24, user's own ask: the download must survive leaving Settings, switching apps and
/// locking the screen, and must leave the phone usable meanwhile). Because it runs as a foreground
/// service the OS keeps the process alive for the duration and shows a persistent progress
/// notification; nothing in the UI has to stay open. On completion it swaps that notification for a
/// tappable "install" one — launching the installer Activity itself from a background service is
/// blocked on modern Android, but a notification tap is a user gesture the OS allows, so the install
/// prompt is reached that way instead.
/// </summary>
[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class UpdateDownloadService : Service
{
    public const string ExtraUrl = "url";
    public const string ExtraVersionCode = "versionCode";
    public const string ExtraVersionName = "versionName";
    public const string ExtraLocalPath = "localPath";
    public const string ActionInstall = "com.companyname.secureapp.presentation.ACTION_INSTALL_UPDATE";

    private const string ChannelId = "secureapp.updates";
    private const int ProgressNotificationId = 4801;
    private const int DoneNotificationId = 4802;
    private const string ProviderAuthority = "com.companyname.secureapp.presentation.updateprovider";

    // Guards against a second tap starting a parallel download of the same file — a foreground
    // service can legitimately receive onStartCommand again while already running.
    private static int _running;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var url = intent?.GetStringExtra(ExtraUrl);
        var versionName = intent?.GetStringExtra(ExtraVersionName) ?? string.Empty;

        if (string.IsNullOrEmpty(url))
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        EnsureChannel();
        StartForegroundCompat(BuildProgressNotification(versionName, 0, indeterminate: true));

        // Already downloading — the fresh onStartCommand is a duplicate tap; keep the first one.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return StartCommandResult.NotSticky;

        _ = Task.Run(() => DownloadAsync(url, versionName));

        // NotSticky: if the OS kills us mid-download it should NOT silently relaunch with a stale
        // intent — the user re-triggers from Settings, which re-checks the version first.
        return StartCommandResult.NotSticky;
    }

    private async Task DownloadAsync(string url, string versionName)
    {
        var destination = Path.Combine(CacheDir!.AbsolutePath, "secureapp-update.apk");
        // Bytes accumulate in a .partial file that deliberately SURVIVES a failure, so the next
        // attempt resumes instead of re-downloading from scratch (user's own ask: a crashed update
        // must not waste data). Only a fully-verified download is renamed to the final .apk.
        var partial = destination + ".partial";
        try
        {
            var have = File.Exists(partial) ? new FileInfo(partial).Length : 0;

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (have > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // 206 => server honoured the Range and is sending only the remainder (append).
            // 200 => server ignored it (or we had nothing) and is sending the whole file (restart).
            var resuming = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!resuming)
                have = 0;

            // Total size of the COMPLETE file, for a correct percentage across a resumed transfer.
            long? total = resuming && response.Content.Headers.ContentRange?.Length is { } full
                ? full
                : response.Content.Headers.ContentLength is { } len ? have + len : null;

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var file = new FileStream(partial, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[81920];
                long soFar = have;
                var lastShownPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    soFar += read;
                    if (total is > 0)
                    {
                        var percent = (int)(soFar * 100 / total.Value);
                        // Re-posting on every 80 KB chunk would thrash the status bar; only when
                        // the whole-number percent actually advances.
                        if (percent != lastShownPercent)
                        {
                            lastShownPercent = percent;
                            NotificationManagerCompat.From(this).Notify(
                                ProgressNotificationId, BuildProgressNotification(versionName, percent, indeterminate: false));
                        }
                    }
                }
            }

            // Guard against a truncated "success": if the server told us the full size and the file
            // on disk is short, treat it as a failure so the partial is kept and resumed, rather
            // than handing a corrupt APK to the installer.
            if (total is > 0 && new FileInfo(partial).Length < total.Value)
                throw new IOException($"Neúplné stažení: {new FileInfo(partial).Length}/{total.Value} B.");

            if (File.Exists(destination)) File.Delete(destination);
            File.Move(partial, destination);
            ShowInstallReady(destination, versionName);
        }
        catch (Exception ex)
        {
            // Deliberately DO NOT delete `partial` — that's the whole point; the next attempt
            // resumes from it. Only the final destination is cleared if a stale one is around.
            Debug.WriteLine($"UpdateDownloadService failed: {ex}");
            ShowFailed();
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            StopForegroundCompat();
            StopSelf();
        }
    }

    private void ShowInstallReady(string localPath, string versionName)
    {
        var file = new Java.IO.File(localPath);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, ProviderAuthority, file);

        // The install Activity is launched by the NOTIFICATION TAP (a user gesture), not by this
        // background service directly — the latter is blocked on Android 10+.
        var install = new Intent(Intent.ActionView);
        install.SetDataAndType(uri, "application/vnd.android.package-archive");
        install.SetFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);

        var pending = PendingIntent.GetActivity(
            this, 0, install, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var done = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Aktualizace připravena")
            .SetContentText($"Verze {versionName} stažena — klepnutím nainstalujte.")
            .SetSmallIcon(ApplicationInfo!.Icon)
            .SetPriority(NotificationCompat.PriorityHigh)
            .SetAutoCancel(true)
            .SetContentIntent(pending)
            .Build();

        NotificationManagerCompat.From(this).Notify(DoneNotificationId, done);
    }

    private void ShowFailed()
    {
        var failed = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Stažení aktualizace se přerušilo")
            .SetContentText("Zkuste to znovu v Nastavení — naváže se tam, kde skončilo.")
            .SetSmallIcon(ApplicationInfo!.Icon)
            .SetPriority(NotificationCompat.PriorityDefault)
            .SetAutoCancel(true)
            .Build();
        NotificationManagerCompat.From(this).Notify(DoneNotificationId, failed);
    }

    private Notification BuildProgressNotification(string versionName, int percent, bool indeterminate)
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Stahuji aktualizaci SecureApp")
            .SetContentText(indeterminate ? $"Verze {versionName}…" : $"Verze {versionName} — {percent} %")
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)
            .SetPriority(NotificationCompat.PriorityLow)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetProgress(100, indeterminate ? 0 : percent, indeterminate);
        return builder.Build();
    }

    private void StartForegroundCompat(Notification notification)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(ProgressNotificationId, notification, global::Android.Content.PM.ForegroundService.TypeDataSync);
        else
            StartForeground(ProgressNotificationId, notification);
    }

    private void StopForegroundCompat()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            StopForeground(StopForegroundFlags.Remove);
        else
#pragma warning disable CA1422
            StopForeground(removeNotification: true);
#pragma warning restore CA1422
    }

    private void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        if (GetSystemService(NotificationService) is not NotificationManager manager) return;
        manager.CreateNotificationChannel(new NotificationChannel(
            ChannelId, "Aktualizace", NotificationImportance.Low)
        {
            Description = "Průběh stahování aktualizací SecureApp"
        });
    }

    /// <summary>Starts the service for a given APK url — the app-facing entry point (see <see cref="AndroidUpdateDownloader"/>).</summary>
    public static void Start(Context context, string url, int versionCode, string versionName)
    {
        var intent = new Intent(context, typeof(UpdateDownloadService));
        intent.PutExtra(ExtraUrl, url);
        intent.PutExtra(ExtraVersionCode, versionCode);
        intent.PutExtra(ExtraVersionName, versionName);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }
}

/// <inheritdoc cref="INativeUpdateDownloader"/>
public sealed class AndroidUpdateDownloader : INativeUpdateDownloader
{
    public void StartBackgroundDownload(string absoluteApkUrl, int versionCode, string versionName)
        => UpdateDownloadService.Start(Microsoft.Maui.ApplicationModel.Platform.AppContext, absoluteApkUrl, versionCode, versionName);
}
