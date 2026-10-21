using System.Text.Json;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Workplace;

/// <summary>Same local-mirror pattern as <see cref="OpicentrumRosterBoardLocalCache"/>, for the volno snapshot.</summary>
public static class OpicentrumVolnoBoardLocalCache
{
    private static string PreferenceKey => Profiles.ActiveProfile.PrefKey("opicentrum_volno_board_local");

    public static OpicentrumVolnoSnapshot? Load()
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, string.Empty);
            return json.Length > 0 ? JsonSerializer.Deserialize<OpicentrumVolnoSnapshot>(json) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(OpicentrumVolnoSnapshot snapshot) =>
        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(snapshot));
}
