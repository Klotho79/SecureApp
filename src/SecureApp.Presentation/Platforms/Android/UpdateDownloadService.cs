using Android.App;
using Android.Content;
using Android.Content.PM;
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
        var versionCode = intent?.GetIntExtra(ExtraVersionCode, 0) ?? 0;

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

        _ = Task.Run(() => DownloadAsync(url, versionCode, versionName));

        // NotSticky: if the OS kills us mid-download it should NOT silently relaunch with a stale
        // intent — the user re-triggers from Settings, which re-checks the version first.
        return StartCommandResult.NotSticky;
    }

    private async Task DownloadAsync(string url, int versionCode, string versionName)
    {
        var destination = Path.Combine(CacheDir!.AbsolutePath, "secureapp-update.apk");
        // Bytes accumulate in a .partial file that deliberately SURVIVES a failure, so the next
        // attempt resumes instead of re-downloading from scratch (user's own ask: a crashed update
        // must not waste data). Only a fully-verified download is renamed to the final .apk.
        var partial = destination + ".partial";
        // Which version `destination` actually is — a completed download that already matches
        // what's being requested again (e.g. the install notification got dismissed/missed) needs
        // zero network at all; only re-download when it's missing or for a different version.
        var versionMarker = destination + ".versioncode";
        try
        {
            if (versionCode > 0 && File.Exists(destination) && File.Exists(versionMarker) &&
                int.TryParse(File.ReadAllText(versionMarker).Trim(), out var cachedVersionCode) &&
                cachedVersionCode == versionCode)
            {
                ShowInstallReady(destination, versionName);
                return;
            }

            // A leftover .partial belongs to whichever version was being fetched when it broke off. If a
            // newer build has been published since, resuming would splice the new APK's tail onto the old
            // one's head (the installer then rejects it as invalid, every time) — so start over instead.
            var partialMarker = partial + ".versioncode";
            if (File.Exists(partial) && versionCode > 0 &&
                !(File.Exists(partialMarker) && int.TryParse(File.ReadAllText(partialMarker).Trim(), out var partialVersionCode) && partialVersionCode == versionCode))
                File.Delete(partial);
            if (versionCode > 0) File.WriteAllText(partialMarker, versionCode.ToString());

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
            if (versionCode > 0) File.WriteAllText(versionMarker, versionCode.ToString());
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
        try
        {
            StartInstallSession(this, localPath, versionName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UpdateDownloadService install session failed: {ex}");
            ShowFailed();
        }
    }

    /// <summary>
    /// Hands the APK to the system through a <c>PackageInstaller</c> session (2026-11-01). The old
    /// <c>ACTION_VIEW</c> flow tied the install to the installer screen, which Android closes when the user
    /// switches to another app — and the install was cancelled with it. Here the APK is written into the
    /// session by this process, so only the one confirmation tap needs the screen; the install itself then
    /// runs inside the system. The outcome arrives in <see cref="UpdateInstallReceiver"/>.
    /// </summary>
    public static void StartInstallSession(Context context, string apkPath, string versionName)
    {
        var installer = context.PackageManager!.PackageInstaller;
        foreach (var stale in installer.MySessions)
            installer.AbandonSession(stale.SessionId);

        var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            parameters.SetRequireUserAction((int)PackageInstallUserAction.NotRequired);
        var sessionId = installer.CreateSession(parameters);
        using var session = installer.OpenSession(sessionId);

        var length = new FileInfo(apkPath).Length;
        using (var input = File.OpenRead(apkPath))
        using (var output = session.OpenWrite("secureapp", 0, length)!)
        {
            input.CopyTo(output);
            session.Fsync(output);
        }

        var result = new Intent(context, typeof(UpdateInstallReceiver));
        result.PutExtra(ExtraVersionName, versionName);
        var pending = PendingIntent.GetBroadcast(context, sessionId, result, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
        session.Commit(pending.IntentSender!);
    }

    internal static void PostInstallNotification(Context context, string title, string text, PendingIntent? tap)
    {
        var builder = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetSmallIcon(context.ApplicationInfo!.Icon)
            .SetPriority(NotificationCompat.PriorityHigh)
            // Stays until the new version has started (ClearIfInstalled) — the confirmation screen can be
            // closed by switching apps and this is the way back to it.
            .SetAutoCancel(false);
        if (tap is not null) builder.SetContentIntent(tap);
        NotificationManagerCompat.From(context).Notify(DoneNotificationId, builder.Build());
    }

    internal static void CancelInstallNotification(Context context) =>
        NotificationManagerCompat.From(context).Cancel(DoneNotificationId);
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

    private static string CachedApkPath(Context context) => Path.Combine(context.CacheDir!.AbsolutePath, "secureapp-update.apk");

    private static bool IsCachedVersion(Context context, int versionCode) =>
        versionCode > 0 && File.Exists(CachedApkPath(context)) && File.Exists(CachedApkPath(context) + ".versioncode") &&
        int.TryParse(File.ReadAllText(CachedApkPath(context) + ".versioncode").Trim(), out var cached) && cached == versionCode;

    /// <summary>
    /// Opens the system installer for an already downloaded APK of exactly <paramref name="versionCode"/>.
    /// Called from a user tap inside the running app (an Activity may be started from the foreground).
    /// </summary>
    public static bool TryInstallCached(Context context, int versionCode, string versionName)
    {
        if (!IsCachedVersion(context, versionCode)) return false;
        var apk = CachedApkPath(context);
        _ = Task.Run(() =>
        {
            try { StartInstallSession(context, apk, versionName); }
            catch (Exception ex) { Debug.WriteLine($"UpdateDownloadService install session failed: {ex}"); }
        });
        return true;
    }
    /// <summary>
    /// Once the running version is at least the downloaded one, the "ready to install" notification and
    /// the cached APK have done their job: remove both (the notification no longer clears itself on tap).
    /// </summary>
    public static void ClearIfInstalled(Context context, int currentVersionCode)
    {
        var apk = CachedApkPath(context);
        var marker = apk + ".versioncode";
        if (!File.Exists(marker) || !int.TryParse(File.ReadAllText(marker).Trim(), out var cached) || cached > currentVersionCode) return;
        CancelInstallNotification(context);
        File.Delete(marker);
        if (File.Exists(apk)) File.Delete(apk);
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

    public bool TryInstallCached(int versionCode, string versionName)
        => UpdateDownloadService.TryInstallCached(Microsoft.Maui.ApplicationModel.Platform.AppContext, versionCode, versionName);
}

/// <summary>
/// Receives the result of the <c>PackageInstaller</c> session from <see cref="UpdateDownloadService.StartInstallSession"/>:
/// "pending user action" carries the system confirmation screen (opened right away when the app is in the
/// foreground, otherwise offered as a notification), a failure is reported with its reason.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class UpdateInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
        var versionName = intent.GetStringExtra(UpdateDownloadService.ExtraVersionName) ?? string.Empty;
        switch (status)
        {
            case PackageInstallStatus.PendingUserAction:
#pragma warning disable CA1422
                var confirm = intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422
                if (confirm is null) return;
                confirm.AddFlags(ActivityFlags.NewTask);
                var tap = PendingIntent.GetActivity(context, 0, confirm, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                UpdateDownloadService.PostInstallNotification(context, "Aktualizace připravena",
                    $"Verze {versionName} — klepnutím potvrďte instalaci. Instalace pak běží na pozadí.", tap);
                try { context.StartActivity(confirm); } catch { /* aplikace není v popředí - zbývá oznámení */ }
                break;
            case PackageInstallStatus.Success:
                UpdateDownloadService.CancelInstallNotification(context);
                break;
            default:
                var reason = status switch
                {
                    PackageInstallStatus.FailureStorage => "V telefonu je málo místa.",
                    PackageInstallStatus.FailureAborted => "Instalace byla zrušena.",
                    _ => intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? "Instalace se nezdařila.",
                };
                UpdateDownloadService.PostInstallNotification(context, "Aktualizace se nenainstalovala",
                    $"{reason} Zkuste to znovu v Nastavení.", null);
                break;
        }
    }
}
