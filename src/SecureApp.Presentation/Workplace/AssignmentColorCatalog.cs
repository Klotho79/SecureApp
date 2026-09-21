using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Per-device, user-overridable color for each <see cref="AssignmentType"/> (2026-09-21, user's own
/// ask: "k tomu bude třeba legenda a uživ nastavení barev" — a legend plus a settings screen to
/// recolor the Workplace/Rozpis type accents, one full color per type rather than separate
/// light/dark or soft/strong pickers). Stored in <see cref="Preferences"/> (same per-device mechanism
/// <c>AppShell</c>'s own tab-visibility toggles already use) as a plain hex string, one key per type —
/// never relay-synced, matching the user's own explicit scoping (per device, like the tab toggles, not
/// shared).
///
/// <see cref="SoftColor"/> is derived, not separately stored — a translucent tint of the same base
/// color (alpha instead of a distinct paired token) so the settings screen only needs one picker per
/// type instead of doubling it for light/dark × soft/strong like the old static-resource tokens did.
/// It reads reasonably in both themes because it blends with whatever surface sits underneath rather
/// than being a fixed opaque value.
/// </summary>
internal static class AssignmentColorCatalog
{
    private const string PreferenceKeyPrefix = "workplace.color.";
    private const float SoftAlpha = 0.18f;

    private static readonly Dictionary<AssignmentType, Color> Defaults = new()
    {
        [AssignmentType.Work] = Color.FromArgb("#1565C0"),
        [AssignmentType.OnCall] = Color.FromArgb("#B5730C"),
        [AssignmentType.BusinessTrip] = Color.FromArgb("#0D4E64"),
        [AssignmentType.Training] = Color.FromArgb("#0D4E64"),
        [AssignmentType.Vacation] = Color.FromArgb("#1F7A5C"),
        [AssignmentType.SickLeave] = Color.FromArgb("#C62828"),
        [AssignmentType.DayOff] = Color.FromArgb("#919191"),
        [AssignmentType.Other] = Color.FromArgb("#919191"),
    };

    public static Color DefaultColor(AssignmentType type) => Defaults[type];

    public static Color BaseColor(AssignmentType type)
    {
        var stored = Preferences.Default.Get(PreferenceKeyPrefix + type, string.Empty);
        return !string.IsNullOrEmpty(stored) && Color.TryParse(stored, out var parsed) ? parsed : Defaults[type];
    }

    public static Color SoftColor(AssignmentType type) => BaseColor(type).WithAlpha(SoftAlpha);

    /// <summary>
    /// The plain <c>CardBorder</c> style's own background (White/Gray950 — see Styles.xaml), used as
    /// the tint for a day with no assignment. Binding <c>Border.BackgroundColor</c> directly to a
    /// per-item color (instead of the old per-type XAML DataTrigger) means an empty day needs an actual
    /// color value here rather than just falling through to the style's own setter, which an explicit
    /// property binding always overrides. Snapshotted from <see cref="AppInfo.RequestedTheme"/> at
    /// render time rather than an AppThemeBinding, so — known, accepted trade-off — a day's tint only
    /// catches up to an OS theme flip on the next re-render (page revisit), not live like every other
    /// AppThemeBinding-driven color in this app.
    /// </summary>
    private static bool IsDarkTheme => AppInfo.Current.RequestedTheme == AppTheme.Dark;

    /// <summary>Matches <c>CardBorder</c>'s own default (White/Gray950) — used by the "Dnes" card, the only one of the three views that doesn't set its own explicit background first.</summary>
    public static Color NoAssignmentCardColor => IsDarkTheme ? Color.FromArgb("#26292C") : Colors.White;

    /// <summary>Matches <c>SurfaceAlt</c>/<c>SurfaceAltDark</c> — used by the Week strip and Month grid, both of which set that as their own explicit background before the (now removed) per-type DataTrigger used to override it.</summary>
    public static Color NoAssignmentSurfaceColor => IsDarkTheme ? Color.FromArgb("#232729") : Color.FromArgb("#EEF2F5");

    public static bool IsCustomized(AssignmentType type) =>
        !string.IsNullOrEmpty(Preferences.Default.Get(PreferenceKeyPrefix + type, string.Empty));

    public static void SetColor(AssignmentType type, Color color) =>
        Preferences.Default.Set(PreferenceKeyPrefix + type, ToHex(color));

    // Manual formatting rather than a Maui.Graphics Color-to-hex helper — the exact method name/output
    // shape (ToHex/ToArgbHex/ToRgbHex, with or without alpha) isn't something to guess at from memory;
    // Color.Red/Green/Blue (0..1 floats) are the stable, well-known part of the API. Round-trips
    // through Color.TryParse in BaseColor above.
    private static string ToHex(Color color) =>
        $"#{ToByte(color.Red):X2}{ToByte(color.Green):X2}{ToByte(color.Blue):X2}";

    private static byte ToByte(float channel) => (byte)Math.Clamp(MathF.Round(channel * 255f), 0, 255);

    public static void ResetColor(AssignmentType type) =>
        Preferences.Default.Remove(PreferenceKeyPrefix + type);
}
