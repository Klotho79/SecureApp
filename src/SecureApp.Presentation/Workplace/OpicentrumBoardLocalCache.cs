using System.Globalization;
using System.Text.Json;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Local (per-device) mirror of the last-seen Opicentrum board snapshot (2026-10-21, user's own ask —
/// opening "Pozice" was "neuvěřitelně pomalé" because the page waited on a relay round trip every
/// time; it must render instantly from whatever this device already has, then reconcile with the
/// relay's copy in the background). Same "Preferences so a page can read it without the network"
/// pattern as <see cref="DutyRosterStore"/>/<see cref="PiccRosterStore"/> — the relay's cache stays
/// the cross-device source of truth, this is purely a fast local copy of it.
/// </summary>
internal sealed class OpicentrumBoardLocalCache<TSnapshot>(string preferenceKeyName) where TSnapshot : class
{
    // Per-profile prefix (Windows multi-profile login) — see ArchivedChatsStore's own remarks.
    private string PreferenceKey(string suffix) => Profiles.ActiveProfile.PrefKey(preferenceKeyName + suffix);

    public TSnapshot? Load(string suffix = "")
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey(suffix), string.Empty);
            return json.Length > 0 ? JsonSerializer.Deserialize<TSnapshot>(json) : null;
        }
        catch
        {
            return null; // corrupt/old format — the next fetch simply overwrites it
        }
    }

    public void Save(TSnapshot snapshot, string suffix = "") =>
        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey(suffix), JsonSerializer.Serialize(snapshot));

    public void Remove(string suffix = "") =>
        Microsoft.Maui.Storage.Preferences.Default.Remove(PreferenceKey(suffix));
}

/// <summary>
/// Local mirror of the roster ("Pozice") board snapshots, one per week (2026-10-24 — the board pages
/// between weeks). Only the few weeks nearest to the week the board opens on are kept.
/// </summary>
public static class OpicentrumRosterBoardLocalCache
{
    private const string KeyName = "opicentrum_roster_board_local";
    private const int KeptWeeks = 8;

    private static readonly OpicentrumBoardLocalCache<OpicentrumRosterBoardSnapshot> Cache = new(KeyName);

    // Preferences cannot list its keys, so the weeks present are remembered here ("yyyyMMdd,yyyyMMdd,…").
    private static string WeeksKey => Profiles.ActiveProfile.PrefKey(KeyName + "_weeks");

    private static string Suffix(DateOnly week) => "_" + week.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static OpicentrumRosterBoardSnapshot? Load(DateOnly week)
    {
        if (Cache.Load(Suffix(week)) is { } snapshot) return snapshot;

        // The single un-suffixed entry the app kept before the board paged between weeks: move it to its
        // week's slot once, so the first open after the update is not empty.
        if (Cache.Load() is not { } legacy) return null;
        Save(legacy);
        Cache.Remove();
        return legacy.WeekOf == week ? legacy : null;
    }

    public static void Save(OpicentrumRosterBoardSnapshot snapshot)
    {
        Cache.Save(snapshot, Suffix(snapshot.WeekOf));
        try
        {
            var prefs = Microsoft.Maui.Storage.Preferences.Default;
            var weeks = prefs.Get(WeeksKey, string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => DateOnly.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
                .OfType<DateOnly>()
                .Append(snapshot.WeekOf)
                .Distinct()
                .ToList();

            var anchor = WeekMath.DefaultViewWeek(DateOnly.FromDateTime(DateTime.Today));
            var keep = weeks.OrderBy(w => Math.Abs(w.DayNumber - anchor.DayNumber)).Take(KeptWeeks).ToHashSet();
            foreach (var dropped in weeks.Where(w => !keep.Contains(w)))
                Cache.Remove(Suffix(dropped));

            prefs.Set(WeeksKey, string.Join(',', keep.Select(w => w.ToString("yyyyMMdd", CultureInfo.InvariantCulture))));
        }
        catch
        {
            // the index only drives clean-up — a failure here must never lose the snapshot just saved
        }
    }
}

/// <summary>Local mirror of the volno (leave) board snapshot.</summary>
public static class OpicentrumVolnoBoardLocalCache
{
    private static readonly OpicentrumBoardLocalCache<OpicentrumVolnoSnapshot> Cache = new("opicentrum_volno_board_local");
    public static OpicentrumVolnoSnapshot? Load() => Cache.Load();
    public static void Save(OpicentrumVolnoSnapshot snapshot) => Cache.Save(snapshot);
}
