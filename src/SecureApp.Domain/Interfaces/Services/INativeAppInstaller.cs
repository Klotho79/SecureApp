namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Platform-specific "install this already-downloaded package file" trigger (2026-09-23) — the
/// actual download (<see cref="IUpdateService.DownloadUpdateAsync"/>) is plain cross-platform HTTP,
/// but handing the resulting file to the OS's own installer is not (Android: a content:// Uri via
/// FileProvider + <c>ACTION_VIEW</c> + the REQUEST_INSTALL_PACKAGES permission; other platforms
/// don't have this concept at all in the same shape). Same per-platform-folder pattern as
/// <c>INativeDlpService</c>/<c>INativeNotificationService</c> — not registered at all on a platform
/// where it makes no sense, rather than a no-op implementation.
/// </summary>
public interface INativeAppInstaller
{
    /// <summary>Launches the OS's own install flow for the file at <paramref name="localFilePath"/> — the user still sees and must confirm the OS's own install prompt; this never silently installs anything.</summary>
    void InstallApk(string localFilePath);
}
