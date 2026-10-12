using System.Globalization;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Local cache of today's on-call PICC doctor's name (2026-10-12) — same "Preferences so the widget
/// can read it without the network" reasoning as <see cref="DutyRosterStore"/>, refreshed from
/// <c>/picc/status</c> by the same periodic sync loop that already runs
/// <c>App.ApplyDevicePolicyAsync</c>/<c>SyncBoardToNotificationsAsync</c>. Deliberately NOT the full
/// roster (no nurses here) — this is the PUBLIC half every device caches, see
/// <c>IPiccRosterService</c>'s own remarks on the split.
/// </summary>
public static class PiccRosterStore
{
    // 2026-10-05-style per-profile prefix (Windows multi-profile login) — see ArchivedChatsStore's own remarks.
    private static string DoctorNameKey => Profiles.ActiveProfile.PrefKey("picc_doctor_name");
    private static string AsOfDateKey => Profiles.ActiveProfile.PrefKey("picc_doctor_as_of");

    /// <summary>Today's doctor name, or null if nothing is cached, nothing was ever published, or the cached value is for a different day than today (never shown stale).</summary>
    public static string? GetTodayDoctorName()
    {
        var prefs = Microsoft.Maui.Storage.Preferences.Default;
        var name = prefs.Get(DoctorNameKey, string.Empty);
        var asOfRaw = prefs.Get(AsOfDateKey, string.Empty);
        if (name.Length == 0 || asOfRaw.Length == 0) return null;
        if (!DateOnly.TryParseExact(asOfRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf)) return null;
        return asOf == DateOnly.FromDateTime(DateTime.Today) ? name : null;
    }

    public static void Save(string? doctorName, DateOnly? asOfDate)
    {
        var prefs = Microsoft.Maui.Storage.Preferences.Default;
        if (string.IsNullOrWhiteSpace(doctorName) || asOfDate is null)
        {
            prefs.Remove(DoctorNameKey);
            prefs.Remove(AsOfDateKey);
            return;
        }
        prefs.Set(DoctorNameKey, doctorName);
        prefs.Set(AsOfDateKey, asOfDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
