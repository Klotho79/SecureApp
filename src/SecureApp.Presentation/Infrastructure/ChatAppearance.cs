namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Per-device chat look (2026-09-26, user's ask): message font size and the colour of your own
/// bubbles. Both chat views bind these via DynamicResource, so a change applies live. With no custom
/// bubble colour the bubbles follow the app accent (Primary / PrimaryDark, per light/dark theme).
/// </summary>
public static class ChatAppearance
{
    public const string FontSizePreferenceKey = "chat_font_size_index";
    public const string BubbleColorPreferenceKey = "chat_bubble_hex";

    public static IReadOnlyList<string> FontSizeChoices { get; } = ["Menší", "Normální", "Větší", "Největší"];
    private static readonly double[] FontSizes = [8, 9, 10.5, 12];

    private static string? _customBubbleHex;

    public static void ApplySaved()
    {
        try
        {
            var prefs = Microsoft.Maui.Storage.Preferences.Default;
            ApplyFontSize(prefs.Get(FontSizePreferenceKey, 1));
            ApplyBubbleColor(prefs.Get(BubbleColorPreferenceKey, string.Empty));
        }
        catch
        {
            // Best-effort — a bad/absent preference must never block startup.
        }
    }

    public static void ApplyFontSize(int index)
    {
        if (Application.Current is not { } app) return;
        app.Resources["ChatMessageFontSize"] = FontSizes[Math.Clamp(index, 0, FontSizes.Length - 1)];
    }

    /// <summary>Empty/invalid hex = follow the app accent.</summary>
    public static void ApplyBubbleColor(string? hex)
    {
        _customBubbleHex = AccentPalette.IsValidHex(hex) ? hex : null;
        Refresh();
    }

    /// <summary>Re-derives the bubble colours — also called on a light/dark theme switch, since the default follows the theme.</summary>
    public static void Refresh()
    {
        if (Application.Current is not { } app) return;
        var res = app.Resources;
        var isDark = app.RequestedTheme == AppTheme.Dark;

        if (_customBubbleHex is not null && AccentPalette.TryParseHex(_customBubbleHex, out var custom))
        {
            var text = Luminance(custom) > 0.55 ? Colors.Black : Colors.White;
            res["ChatOwnBubbleColor"] = custom;
            res["ChatOwnBubbleTextColor"] = text;
            res["ChatOwnBubbleMetaColor"] = text.WithAlpha(0.75f);
            return;
        }

        res["ChatOwnBubbleColor"] = ResourceColor(res, isDark ? "PrimaryDark" : "Primary");
        res["ChatOwnBubbleTextColor"] = isDark ? ResourceColor(res, "PrimaryDarkText") : Colors.White;
        res["ChatOwnBubbleMetaColor"] = isDark ? ResourceColor(res, "PrimaryDarkText") : Color.FromArgb("#CFE7F0");
    }

    private static Color ResourceColor(ResourceDictionary res, string key)
        => res.TryGetValue(key, out var value) && value is Color color ? color : Colors.Gray;

    private static double Luminance(Color c) => 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue;
}
