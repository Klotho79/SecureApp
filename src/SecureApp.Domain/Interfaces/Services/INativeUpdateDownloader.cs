namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Platform-specific "download this update in the background and prompt to install when done"
/// trigger (2026-09-24, user's own ask: the download must keep running when the user leaves the
/// Settings screen or switches to another app, and must not tie up the phone). On Android this
/// hands off to a foreground <c>Service</c> with its own progress notification, so the download is
/// owned by the OS service lifecycle rather than a page's ViewModel — leaving Settings, locking the
/// screen, or opening another app no longer aborts it. Not registered on platforms without this
/// concept (same per-platform pattern as <see cref="INativeAppInstaller"/>); callers fall back to
/// the in-process <see cref="IUpdateService.DownloadUpdateAsync"/> path there.
/// </summary>
public interface INativeUpdateDownloader
{
    /// <summary>
    /// Starts (or no-ops if one is already running) a background download of the APK at
    /// <paramref name="absoluteApkUrl"/>. Returns immediately — progress and the eventual
    /// tap-to-install prompt are surfaced through a system notification, not back through this call.
    /// </summary>
    void StartBackgroundDownload(string absoluteApkUrl, int versionCode, string versionName);
}
