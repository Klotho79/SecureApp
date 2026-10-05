using System.Text.Json;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.Contacts;

/// <summary>
/// Offline name→phone lookup for the widget's duty-roster phone icon (2026-09-29, user's ask: "ve
/// wigetu rovnou ke sloužícím přidej pictogram a na ktery když se klikne skopiruje služební tel do tel
/// k vytočení"). The widget must never make a network call on refresh (same reasoning as
/// <see cref="DutyRosterStore"/>'s own remarks), so this snapshots <see cref="ISharedContactService"/>'s
/// result to Preferences on every background sweep, and <see cref="TryFindPhone"/> only ever reads that
/// snapshot. Matching reuses <see cref="OpicentrumParsing.NamesMatch"/> — the same portal-name/company-
/// directory-name mismatch problem (order, diacritics, titles) this class was already built to solve.
/// </summary>
public static class ContactPhoneCache
{
    // 2026-10-05 — Windows multi-profile login: prefixed per-profile (no-op on Android, where the
    // widget this exists for actually runs — see Profiles.ActiveProfile.PrefKey's own remarks).
    private static string PreferenceKey => Profiles.ActiveProfile.PrefKey("shared_contact_phone_cache");

    public static async Task RefreshAsync(ISharedContactService service)
    {
        try
        {
            var contacts = await service.FetchAsync();
            var entries = contacts
                .Where(c => !string.IsNullOrWhiteSpace(c.Phone))
                .Select(c => new Entry(c.DisplayName, c.Phone!))
                .ToList();
            Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(entries));
        }
        catch
        {
            // Best-effort — a stale/empty cache just means the widget's phone icon doesn't show up
            // for a name it can't resolve yet; never worth surfacing as an error.
        }
    }

    /// <summary>The first dialable number ("+420777123456") for a duty-roster name, or null if no shared contact matches or the match's own number has no complete/dialable part (e.g. only an extension).</summary>
    public static string? TryFindPhone(string dutyName)
    {
        foreach (var entry in Load())
        {
            if (OpicentrumParsing.NamesMatch(entry.Name, dutyName))
            {
                var dial = PhoneNumberFormat.FirstDialable(entry.Phone);
                if (dial is not null) return dial;
            }
        }
        return null;
    }

    private static List<Entry> Load()
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, string.Empty);
            if (json.Length > 0 && JsonSerializer.Deserialize<List<Entry>>(json) is { } parsed)
                return parsed;
        }
        catch
        {
            // Corrupt/old format — start over; the next background sweep refills it.
        }
        return [];
    }

    private sealed record Entry(string Name, string Phone);
}
