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

    /// <summary>
    /// Downloads the currently-hosted update to local app storage and returns its path — best-effort
    /// progress reporting via <paramref name="onProgress"/> (0.0–1.0), since this can be a large file
    /// on a slow connection. On Windows (2026-10-14), transparently prefers a much smaller delta
    /// containing only the files that actually changed since this install, falling back to the full
    /// build whenever that isn't possible — see <c>UpdateService</c>'s own remarks. Android always
    /// gets the full APK; there's no equivalent delta mechanism for a sideloaded package.
    /// </summary>
    Task<string> DownloadUpdateAsync(IProgress<double>? onProgress = null, CancellationToken ct = default);

    /// <summary>The absolute <c>http(s)://…/download/android</c> URL for the currently-configured relay — needed by <see cref="INativeUpdateDownloader"/>, whose OS service downloads directly rather than going back through this cross-platform type.</summary>
    Task<string> GetDownloadUrlAsync(CancellationToken ct = default);
}
