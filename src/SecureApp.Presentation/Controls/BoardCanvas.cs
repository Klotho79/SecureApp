namespace SecureApp.Presentation.Controls;

/// <summary>Zoom of the Pozice and Volno boards (both tabs share one value, remembered per device); the views scale their canvas and size by it.</summary>
internal static class BoardZoom
{
    public const float Min = 1f;
    public const float Max = 2.5f;
    private const string PreferenceKey = "opicentrum_board_zoom";

    public static float Value { get; private set; } = Math.Clamp((float)Preferences.Default.Get(PreferenceKey, 1.0), Min, Max);

    public static event Action? Changed;

    /// <summary>Sets the zoom (pinch gesture); not persisted until <see cref="Save"/> at the end of the gesture.</summary>
    public static void Set(float zoom)
    {
        var next = Math.Clamp(zoom, Min, Max);
        if (next == Value) return;
        Value = next;
        Changed?.Invoke();
    }

    public static void Save() => Preferences.Default.Set(PreferenceKey, (double)Value);
}

/// <summary>
/// Two-finger pinch on a board's scrolling body (2026-11-07, user: "chci zvetsovat gestem"): zooms the shared
/// <see cref="BoardZoom"/> and keeps the point between the fingers where it was by scrolling the page's ScrollView
/// once the body has been re-laid-out at its new size. The delta handling copies <see cref="PinchZoomController"/>
/// (the first update is dropped, wild samples rejected), because MAUI reports pinch scale per update.
/// </summary>
internal sealed class BoardPinchZoom
{
    private readonly View _body;
    private readonly ScrollView _scroll;
    private bool _skipFirst;
    private double _smoothed = 1;
    private double _focalX, _focalY, _viewX, _viewY;
    private (double X, double Y)? _pendingScroll;

    public BoardPinchZoom(View body, ScrollView scroll)
    {
        _body = body;
        _scroll = scroll;
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        body.GestureRecognizers.Add(pinch);
        body.SizeChanged += OnBodySizeChanged;
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _skipFirst = true;
                _smoothed = 1;
                // The point under the fingers in unzoomed board units, and where it sits in the viewport.
                var zoom = BoardZoom.Value;
                var contentX = e.ScaleOrigin.X * _body.Width;
                var contentY = e.ScaleOrigin.Y * _body.Height;
                _focalX = contentX / zoom;
                _focalY = contentY / zoom;
                _viewX = contentX - _scroll.ScrollX;
                _viewY = contentY - _scroll.ScrollY;
                break;

            case GestureStatus.Running:
                if (_skipFirst) { _skipFirst = false; return; }
                if (e.Scale < 0.8 || e.Scale > 1.25) return;
                _smoothed = _smoothed * 0.5 + e.Scale * 0.5;
                BoardZoom.Set((float)(BoardZoom.Value * _smoothed));
                _pendingScroll = (Math.Max(0, _focalX * BoardZoom.Value - _viewX), Math.Max(0, _focalY * BoardZoom.Value - _viewY));
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                BoardZoom.Save();
                break;
        }
    }

    private async void OnBodySizeChanged(object? sender, EventArgs e)
    {
        if (_pendingScroll is not { } target) return;
        _pendingScroll = null;
        try { await _scroll.ScrollToAsync(target.X, target.Y, false); }
        catch { /* the scroll position is a nicety; the zoom itself already applied */ }
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
