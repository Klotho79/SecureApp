namespace SecureApp.Presentation.Controls;

/// <summary>
/// Pinch-to-zoom + one-finger pan for a single <see cref="Image"/>, double-tap to reset. Extracted
/// from <c>DocumentViewerPage</c>'s own gesture code (2026-09-30) so CMS inline images
/// (CMS_PLAN.md, phase 3, "pinch-zoom obrázku") get that exact same proven math — several real bugs
/// were already found and fixed there (zoom jumping to a corner, slow/sticky panning, jitter at high
/// zoom) — instead of a second, divergent implementation. See that page's own remarks for the full
/// history behind each quirk below if this ever needs touching again.
/// </summary>
public sealed class PinchZoomController
{
    private const double MaxScale = 3;

    private readonly Image _image;
    private double _currentScale = 1;
    private double _panX;
    private double _panY;
    private bool _ignoreNextPinchDelta;
    private double _smoothedDelta = 1;

    public PinchZoomController(Image image)
    {
        _image = image;
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (_, _) => Reset();
        image.GestureRecognizers.Add(pinch);
        image.GestureRecognizers.Add(pan);
        image.GestureRecognizers.Add(doubleTap);
    }

    public void Reset()
    {
        _currentScale = 1;
        _panX = 0;
        _panY = 0;
        _smoothedDelta = 1;
        _image.Scale = 1;
        _image.TranslationX = 0;
        _image.TranslationY = 0;
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _ignoreNextPinchDelta = true;
            _smoothedDelta = 1;
            return;
        }

        if (e.Status != GestureStatus.Running) return;

        if (_ignoreNextPinchDelta)
        {
            _ignoreNextPinchDelta = false;
            return;
        }

        // Reject obviously-noisy platform touch samples outright rather than clamp-and-apply a
        // squashed version of them (measured on-device: raw e.Scale alternating ~3x/~0.3x near max
        // zoom with no real finger motion behind it).
        if (e.Scale < 0.8 || e.Scale > 1.25) return;

        // Light exponential smoothing of the accepted delta irons out jitter that's still visible
        // even after the rejection above, without amplifying it (an earlier Math.Pow amplification
        // attempt made ordinary per-frame jitter read as "skace tam a zpet").
        _smoothedDelta = _smoothedDelta * 0.5 + e.Scale * 0.5;
        _currentScale = Math.Clamp(_currentScale * _smoothedDelta, 1, MaxScale);
        _image.Scale = _currentScale;
        ClampTranslation();
        // Pinch has no "total since gesture start" value the way Pan does — its own translation
        // nudges are ad hoc, so the clamped result IS the new baseline immediately, not just for display.
        _panX = _image.TranslationX;
        _panY = _image.TranslationY;
    }

    /// <summary>One-finger drag to keep panning after the pinch itself ends. No-op while unzoomed.</summary>
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_currentScale <= 1) return;

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                // e.TotalX/Y is the total distance panned since THIS gesture started, so _panX/_panY
                // (the position before this gesture) must stay untouched for the whole Running phase —
                // writing the clamped display value back into that baseline every callback compounds
                // into hitting the edge almost immediately, which reads as the drag "sticking".
                _image.TranslationX = _panX + e.TotalX;
                _image.TranslationY = _panY + e.TotalY;
                ClampTranslation();
                break;
            case GestureStatus.Completed:
                _panX = _image.TranslationX;
                _panY = _image.TranslationY;
                break;
        }
    }

    /// <summary>
    /// The Image keeps MAUI's own default AnchorX/AnchorY (0.5, 0.5) — Scale always expands it
    /// symmetrically outward from its middle, so the overflow past each edge is
    /// Width/Height * (scale - 1) split evenly on both sides, and the pannable range is ± half that.
    /// </summary>
    private void ClampTranslation()
    {
        var maxX = _image.Width * (_currentScale - 1) / 2;
        var maxY = _image.Height * (_currentScale - 1) / 2;
        _image.TranslationX = Math.Clamp(_image.TranslationX, -maxX, maxX);
        _image.TranslationY = Math.Clamp(_image.TranslationY, -maxY, maxY);
    }
}
