namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Admin-only debugging switch that turns the DLP screenshot block off on THIS device (2026-10-11,
/// user's ask: "pro účely ladění pro admina dej možnost vypnutí ofocení displeje"). Stored as an
/// expiry time, not a plain bool: a forgotten switch must not leave a device unprotected for good,
/// so the block comes back on its own after <see cref="Duration"/> (at the next launch, or the next
/// time Settings is opened).
/// </summary>
public static class ScreenCaptureDebugOverride
{
    private const string ExpiresUtcPreferenceKey = "dlp_capture_allowed_until_utc";
    public static readonly TimeSpan Duration = TimeSpan.FromHours(24);

    /// <summary>Until when screenshots are allowed, or null when the block is on (the default).</summary>
    public static DateTime? AllowedUntilUtc
    {
        get
        {
            try
            {
                var ticks = Microsoft.Maui.Storage.Preferences.Default.Get(ExpiresUtcPreferenceKey, 0L);
                return ticks > DateTime.UtcNow.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool IsActive => AllowedUntilUtc is not null;

    public static void Enable() =>
        Microsoft.Maui.Storage.Preferences.Default.Set(ExpiresUtcPreferenceKey, DateTime.UtcNow.Add(Duration).Ticks);

    public static void Disable() =>
        Microsoft.Maui.Storage.Preferences.Default.Remove(ExpiresUtcPreferenceKey);
}
