namespace SecureApp.Presentation.Controls;

/// <summary>Zoom of the Pozice and Volno boards (both tabs share one value, remembered per device); the views scale their canvas and size by it.</summary>
internal static class BoardZoom
{
    public const float Min = 1f;
    public const float Max = 2.5f;
    public const float Step = 0.25f;
    private const string PreferenceKey = "opicentrum_board_zoom";

    public static float Value { get; private set; } = Math.Clamp((float)Preferences.Default.Get(PreferenceKey, 1.0), Min, Max);

    public static event Action? Changed;

    public static void Change(float delta)
    {
        var next = Math.Clamp(MathF.Round((Value + delta) / Step) * Step, Min, Max);
        if (next == Value) return;
        Value = next;
        Preferences.Default.Set(PreferenceKey, (double)next);
        Changed?.Invoke();
    }
}

/// <summary>Small helpers shared by the canvas-drawn Opicentrum boards (<see cref="VolnoBoardView"/>, <see cref="PositionsBoardView"/>).</summary>
internal static class BoardCanvas
{
    /// <summary>An app colour token by resource key, re-read on every draw so a theme switch only needs an invalidate.</summary>
    public static Color Resource(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : fallback;

    /// <summary>Single line that fits <paramref name="maxWidth"/>, cut with an ellipsis like a Label's TailTruncation.</summary>
    public static string Fit(string text, float maxWidth, Func<string, float> measureWidth)
    {
        if (measureWidth(text) <= maxWidth) return text;
        var length = text.Length;
        while (length > 1 && measureWidth(text[..length] + "…") > maxWidth)
            length--;
        return text[..length] + "…";
    }
}
