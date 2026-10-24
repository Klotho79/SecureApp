namespace SecureApp.Presentation.Controls;

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
