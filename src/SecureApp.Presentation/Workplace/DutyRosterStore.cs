using System.Globalization;
using System.Text.Json;

namespace SecureApp.Presentation.Workplace;

/// <summary>One person on duty: the sluzby7.php slot label (or "Odpolední 13-21 h" for the posunutá služba) and their name.</summary>
public sealed record DutyEntry(string Position, string Name);

/// <summary>
/// The whole team's duty roster per day (2026-09-26, user's ask: the widget can switch from the
/// rozpis to today's full duty list). The Opicentrum sync only writes the user's OWN assignments to
/// the database; this keeps the everyone-view it already downloads, in Preferences so the widget can
/// read it without the network.
/// </summary>
public static class DutyRosterStore
{
    private const string PreferenceKey = "opicentrum_duty_roster";
    private const int KeepPastDays = 7;

    public static IReadOnlyList<DutyEntry> Get(DateOnly date)
        => Load().TryGetValue(Key(date), out var day) ? day : [];

    /// <summary>Replaces every day in [rangeStart, rangeEnd] with the freshly synced data; days outside the range are kept, days older than a week dropped.</summary>
    public static void Save(DateOnly rangeStart, DateOnly rangeEnd, IReadOnlyDictionary<DateOnly, List<DutyEntry>> duty)
    {
        var all = Load();
        var oldest = DateOnly.FromDateTime(DateTime.Today).AddDays(-KeepPastDays);
        foreach (var key in all.Keys.ToList())
        {
            var date = DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (date < oldest || (date >= rangeStart && date <= rangeEnd))
                all.Remove(key);
        }

        foreach (var (date, entries) in duty)
        {
            if (date >= rangeStart && date <= rangeEnd && entries.Count > 0)
                all[Key(date)] = entries;
        }

        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(all));
    }

    private static Dictionary<string, List<DutyEntry>> Load()
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, string.Empty);
            if (json.Length > 0 && JsonSerializer.Deserialize<Dictionary<string, List<DutyEntry>>>(json) is { } parsed)
                return parsed;
        }
        catch
        {
            // Corrupt/old format — start over; the next sync refills it.
        }
        return [];
    }

    private static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
