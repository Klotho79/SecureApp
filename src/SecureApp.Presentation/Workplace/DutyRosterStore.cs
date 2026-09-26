using System.Globalization;
using System.Text.Json;

namespace SecureApp.Presentation.Workplace;

/// <summary>Everyone on duty on one day: the sluzby7.php slots and the pracoviste.php "Odpolední 13-21 h" (posunutá služba) row.</summary>
public sealed record DutyRosterDay(IReadOnlyList<string> OnDuty, IReadOnlyList<string> Shifted);

/// <summary>
/// The whole team's duty roster per day (2026-09-26, user's ask: the widget shows who is on duty
/// today). The Opicentrum sync only writes the user's OWN assignments to the database; this keeps the
/// everyone-view it already downloads, in Preferences so the widget can read it without the network.
/// </summary>
public static class DutyRosterStore
{
    private const string PreferenceKey = "opicentrum_duty_roster";
    private const int KeepPastDays = 7;

    public static DutyRosterDay? Get(DateOnly date)
        => Load().TryGetValue(Key(date), out var day) ? day : null;

    /// <summary>Replaces every day in [rangeStart, rangeEnd] with the freshly synced data; days outside the range are kept, days older than a week dropped.</summary>
    public static void Save(DateOnly rangeStart, DateOnly rangeEnd, IReadOnlyDictionary<DateOnly, List<string>> onDuty, IReadOnlyDictionary<DateOnly, List<string>> shifted)
    {
        var all = Load();
        var oldest = DateOnly.FromDateTime(DateTime.Today).AddDays(-KeepPastDays);
        foreach (var key in all.Keys.ToList())
        {
            var date = DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (date < oldest || (date >= rangeStart && date <= rangeEnd))
                all.Remove(key);
        }

        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            var duty = onDuty.GetValueOrDefault(date) ?? [];
            var late = shifted.GetValueOrDefault(date) ?? [];
            if (duty.Count > 0 || late.Count > 0)
                all[Key(date)] = new DutyRosterDay(duty, late);
        }

        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(all));
    }

    /// <summary>"Doc. Tomáš Gabrhelík" → "Gabrhelík" — the widget line has room for surnames only.</summary>
    public static string Surname(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (!parts[i].EndsWith('.') && !parts[i].EndsWith(','))
                return parts[i];
        }
        return fullName;
    }

    private static Dictionary<string, DutyRosterDay> Load()
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, string.Empty);
            if (json.Length > 0 && JsonSerializer.Deserialize<Dictionary<string, DutyRosterDay>>(json) is { } parsed)
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
