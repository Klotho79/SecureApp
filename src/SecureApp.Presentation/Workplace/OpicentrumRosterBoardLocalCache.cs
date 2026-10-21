using System.Text.Json;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Local (per-device) mirror of the last-seen Opicentrum roster board snapshot (2026-10-21, user's
/// own ask — opening "Pozice" was "neuvěřitelně pomalé" because the page waited on a relay round
/// trip every time; it must render instantly from whatever this device already has, then reconcile
/// with the relay's copy in the background). Same "Preferences so a page can read it without the
/// network" pattern as <see cref="DutyRosterStore"/>/<see cref="PiccRosterStore"/> — the relay's own
/// cache (<c>IOpicentrumRosterBoardService</c>) stays the cross-device source of truth, this is
/// purely a fast local copy of it.
/// </summary>
public static class OpicentrumRosterBoardLocalCache
{
    // 2026-10-05-style per-profile prefix (Windows multi-profile login) — see ArchivedChatsStore's own remarks.
    private static string PreferenceKey => Profiles.ActiveProfile.PrefKey("opicentrum_roster_board_local");

    public static OpicentrumRosterBoardSnapshot? Load()
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, string.Empty);
            return json.Length > 0 ? JsonSerializer.Deserialize<OpicentrumRosterBoardSnapshot>(json) : null;
        }
        catch
        {
            return null; // corrupt/old format — the next fetch simply overwrites it
        }
    }

    public static void Save(OpicentrumRosterBoardSnapshot snapshot) =>
        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(snapshot));
}
