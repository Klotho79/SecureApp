namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// User-chosen accent colour (2026-09-24, Phase 4 — easy customization). The app's colours are all
/// <c>StaticResource</c>, so a live swap would need converting every page to DynamicResource (large,
/// risky); instead the chosen accent is stored and re-applied at startup by overwriting the
/// accent-family colour resources on <see cref="Application.Resources"/> before any page is built —
/// so it takes effect on the next launch (documented as such in Settings). Only the accent family is
/// themed (Primary / AccentStrong and their dark-mode variants + Tertiary): those drive the buttons,
/// titles, tab highlight and chips, i.e. everything the user reads as "the app's colour".
/// </summary>
public static class AccentPalette
{
    public const string PreferenceKey = "app_accent_hex";

    /// <summary>Preset accents offered in Settings: display name + base hex. The first is the app's original teal, so "default" is just the first preset.</summary>
    public static IReadOnlyList<(string Name, string Hex)> Presets { get; } =
    [
        ("Tyrkysová", "#14688A"),
        ("Modrá",     "#1565C0"),
        ("Zelená",    "#2E7D52"),
        ("Fialová",   "#6A4CA5"),
        ("Vínová",    "#B23A48"),
        ("Grafit",    "#455A64"),
    ];

    public static void ApplySaved()
    {
        try { Apply(Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, string.Empty)); }
        catch { /* best-effort — a bad/absent preference must never block startup */ }
    }

    /// <summary>Overwrites the accent-family resources from a base hex. Empty/invalid hex restores nothing (the Colors.xaml defaults stay in effect).</summary>
    public static void Apply(string? baseHex)
    {
        if (Application.Current is not { } app) return;
        if (string.IsNullOrWhiteSpace(baseHex) || !TryParseHex(baseHex, out var baseColor)) return;

        var res = app.Resources;
        // Light theme: the chosen colour as-is for Primary, a darker shade for the readable
        // AccentStrong text/icon colour. Dark theme: lightened variants so they stay legible on a
        // dark surface, matching how Colors.xaml's own PrimaryDark/AccentStrongDark relate to their
        // light counterparts.
        res["Primary"] = baseColor;
        res["Tertiary"] = Darken(baseColor, 0.30);
        res["AccentStrong"] = Darken(baseColor, 0.22);
        res["PrimaryDark"] = Lighten(baseColor, 0.45);
        res["AccentStrongDark"] = Lighten(baseColor, 0.55);
    }

    public static bool IsValidHex(string? hex) => TryParseHex(hex, out _);

    private static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        if (s.Length != 7) return false; // require #RRGGBB
        try { color = Color.FromArgb(s); return true; }
        catch { return false; }
    }

    private static Color Lighten(Color c, double amount)
        => Color.FromRgba(
            c.Red + (1 - c.Red) * amount,
            c.Green + (1 - c.Green) * amount,
            c.Blue + (1 - c.Blue) * amount,
            c.Alpha);

    private static Color Darken(Color c, double amount)
        => Color.FromRgba(c.Red * (1 - amount), c.Green * (1 - amount), c.Blue * (1 - amount), c.Alpha);
}
