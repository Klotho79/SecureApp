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
    private string PreferenceKey => Profiles.ActiveProfile.PrefKey(preferenceKeyName);

    public TSnapshot? Load()
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, string.Empty);
            return json.Length > 0 ? JsonSerializer.Deserialize<TSnapshot>(json) : null;
        }
        catch
        {
            return null; // corrupt/old format — the next fetch simply overwrites it
        }
    }

    public void Save(TSnapshot snapshot) =>
        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(snapshot));
}

/// <summary>Local mirror of the roster ("Pozice") board snapshot.</summary>
public static class OpicentrumRosterBoardLocalCache
{
    private static readonly OpicentrumBoardLocalCache<OpicentrumRosterBoardSnapshot> Cache = new("opicentrum_roster_board_local");
    public static OpicentrumRosterBoardSnapshot? Load() => Cache.Load();
    public static void Save(OpicentrumRosterBoardSnapshot snapshot) => Cache.Save(snapshot);
}

/// <summary>Local mirror of the volno (leave) board snapshot.</summary>
public static class OpicentrumVolnoBoardLocalCache
{
    private static readonly OpicentrumBoardLocalCache<OpicentrumVolnoSnapshot> Cache = new("opicentrum_volno_board_local");
    public static OpicentrumVolnoSnapshot? Load() => Cache.Load();
    public static void Save(OpicentrumVolnoSnapshot snapshot) => Cache.Save(snapshot);
}
