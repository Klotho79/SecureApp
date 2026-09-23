using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Self-update system (2026-09-23, user's own ask: the app itself checks for and offers a newer
/// version, hosted on the Pi relay's own <c>/download/android</c>/<c>/download/android/version</c>
/// endpoints — see <c>SecureApp.Relay</c>'s own remarks). Cross-platform by contract (plain HTTP), a
/// single implementation covers every platform; only actually *installing* a downloaded APK is
/// platform-specific — see <see cref="INativeAppInstaller"/>.
/// </summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>Downloads the currently-hosted APK to local app storage and returns its path — best-effort progress reporting via <paramref name="onProgress"/> (0.0–1.0), since this can be a large file on a slow connection.</summary>
    Task<string> DownloadUpdateAsync(IProgress<double>? onProgress = null, CancellationToken ct = default);
}
