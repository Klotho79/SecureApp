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
        // Android never delivers MAUI's pinch inside a native ScrollView (it takes over the touches), so there the
        // platform BoardPinchHook feeds BeginNative/ScaleNative/EndNative below instead.
        if (!OperatingSystem.IsAndroid())
        {
            var pinch = new PinchGestureRecognizer();
            pinch.PinchUpdated += OnPinchUpdated;
            body.GestureRecognizers.Add(pinch);
        }

        body.SizeChanged += OnBodySizeChanged;
#if WINDOWS
        // A PC without a touch screen: Ctrl + mouse wheel zooms (the plain wheel keeps scrolling).
        scroll.HandlerChanged += (_, _) => TryAttachWheel();
        TryAttachWheel();
#endif
    }

#if WINDOWS
    private bool _wheelAttached;

    private void TryAttachWheel()
    {
        if (_wheelAttached || _scroll.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
            return;

        _wheelAttached = true;
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWheel), true);
    }

    private void OnWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(global::Windows.System.VirtualKey.Control)
            .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!ctrl)
            return;

        var point = e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender);
        var notches = point.Properties.MouseWheelDelta / 120.0;
        BeginNative(point.Position.X, point.Position.Y);
        ScaleNative(Math.Pow(1.1, notches), point.Position.X, point.Position.Y);
        EndNative();
        e.Handled = true;
    }
#endif

    /// <summary>A pinch starts with the fingers' midpoint at (<paramref name="viewX"/>, <paramref name="viewY"/>) in the visible board area (dp).</summary>
    public void BeginNative(double viewX, double viewY)
    {
        var zoom = BoardZoom.Value;
        _viewX = viewX;
        _viewY = viewY;
        _focalX = (viewX + _scroll.ScrollX) / zoom;
        _focalY = (viewY + _scroll.ScrollY) / zoom;
    }

    /// <summary>Applies one step of the pinch (<paramref name="factor"/> is relative to the previous step) and keeps the board point under the fingers.</summary>
    public void ScaleNative(double factor, double viewX, double viewY)
    {
        var before = BoardZoom.Value;
        BoardZoom.Set((float)(before * factor));
        _viewX = viewX;
        _viewY = viewY;
        if (BoardZoom.Value != before)
            _pendingScroll = (Math.Max(0, _focalX * BoardZoom.Value - _viewX), Math.Max(0, _focalY * BoardZoom.Value - _viewY));
    }

    public void EndNative() => BoardZoom.Save();

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

    /// <summary>Greedy word wrap; a single word wider than the space (e.g. a capped name column at high zoom) is split with a hyphen, not cut off.</summary>
    public static string[] WrapText(string text, float maxWidth, Func<string, float> measureWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (measureWidth(candidate) <= maxWidth) { current = candidate; continue; }
            if (current.Length > 0) lines.Add(current);
            current = word;
            while (current.Length > 1 && measureWidth(current) > maxWidth)
            {
                var take = current.Length - 1;
                while (take > 1 && measureWidth(current[..take] + "-") > maxWidth) take--;
                // Never leave just a letter or two for the next line ("Baláži-k" reads worse than "Balá-žik").
                if (current.Length >= 5 && current.Length - take < 3) take = current.Length - 3;
                lines.Add(current[..take] + "-");
                current = current[take..];
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines.Count == 0 ? [string.Empty] : [.. lines];
    }
}
