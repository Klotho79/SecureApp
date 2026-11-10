using Android.OS;
using Android.Views;
using Microsoft.Maui.ApplicationModel;
using SecureApp.Presentation.Controls;
using AView = Android.Views.View;

namespace SecureApp.Presentation.Platforms.Android;

/// <summary>
/// Pinch-to-zoom for the Opicentrum boards on Android (2026-11-10, user: "pořád nejde zvětšovat rozpis gestem").
/// MAUI's <c>PinchGestureRecognizer</c> never fires there: the boards sit inside a native two-way scroll view, which
/// takes over the touch stream as soon as a finger moves and cancels the child that was listening for the pinch (the
/// document viewer works only because its image is not inside a ScrollView). So the touches are observed one level
/// above — in <c>MainActivity.DispatchTouchEvent</c>, before any view sees them — by a platform
/// <see cref="ScaleGestureDetector"/>. When a two-finger pinch starts over a registered board, the views get a CANCEL
/// once (so the scroll view lets go) and the rest of that touch is swallowed, so the page does not also pan.
/// </summary>
internal sealed class BoardPinchHook : Java.Lang.Object, ScaleGestureDetector.IOnScaleGestureListener
{
    public static BoardPinchHook Instance { get; } = new();

    private readonly List<(BoardPinchZoom Zoom, Microsoft.Maui.Controls.VisualElement Container)> _targets = [];
    private ScaleGestureDetector? _detector;
    private Func<MotionEvent, bool>? _dispatchToWindow;
    private BoardPinchZoom? _active;
    private AView? _activeView;
    private bool _swallowing;

    /// <summary>Registers a board for pinch zoom while its page is on screen; <paramref name="container"/> is the element the board is drawn in (its on-screen rectangle is the pinch area).</summary>
    public void Register(BoardPinchZoom zoom, Microsoft.Maui.Controls.VisualElement container)
    {
        _targets.RemoveAll(t => ReferenceEquals(t.Zoom, zoom));
        _targets.Add((zoom, container));
    }

    public void Clear()
    {
        _targets.Clear();
        _active = null;
        _activeView = null;
        _swallowing = false;
    }

    /// <summary>Returns true when the event was consumed by a pinch (the caller must not pass it on).</summary>
    public bool Dispatch(MotionEvent ev, Func<MotionEvent, bool> dispatchToWindow)
    {
        if (_targets.Count == 0)
            return false;

        try
        {
            _dispatchToWindow = dispatchToWindow;
            _detector ??= new ScaleGestureDetector(Platform.AppContext, this) { QuickScaleEnabled = false };

            if (ev.ActionMasked == MotionEventActions.Down)
            {
                _swallowing = false;
                _active = null;
            }

            _detector.OnTouchEvent(ev);

            var swallow = _swallowing;
            if (ev.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                _swallowing = false;
                _active = null;
            }

            return swallow;
        }
        catch
        {
            // Touch handling must never break because of the zoom nicety.
            _swallowing = false;
            return false;
        }
    }

    public bool OnScaleBegin(ScaleGestureDetector detector)
    {
        foreach (var (zoom, container) in _targets)
        {
            if (container.Handler?.PlatformView is not AView view || !view.IsShown || view.Width == 0)
                continue;

            var location = new int[2];
            view.GetLocationInWindow(location);
            var x = detector.FocusX - location[0];
            var y = detector.FocusY - location[1];
            if (x < 0 || y < 0 || x > view.Width || y > view.Height)
                continue;

            var density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            _active = zoom;
            _activeView = view;
            zoom.BeginNative(x / density, y / density);

            // Whatever view had the first finger (the scroll view, a chip) is released with one CANCEL;
            // from here on the touch belongs to the pinch.
            var now = SystemClock.UptimeMillis();
            var cancel = MotionEvent.Obtain(now, now, MotionEventActions.Cancel, detector.FocusX, detector.FocusY, 0);
            try { _dispatchToWindow?.Invoke(cancel); }
            finally { cancel.Recycle(); }

            _swallowing = true;
            return true;
        }

        return false;
    }

    public bool OnScale(ScaleGestureDetector detector)
    {
        if (_active is not { } zoom || _activeView is not { } view)
            return false;

        var location = new int[2];
        view.GetLocationInWindow(location);
        var density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        var factor = Math.Clamp(detector.ScaleFactor, 0.8f, 1.25f);
        zoom.ScaleNative(factor, (detector.FocusX - location[0]) / density, (detector.FocusY - location[1]) / density);
        return true;
    }

    public void OnScaleEnd(ScaleGestureDetector detector)
    {
        _active?.EndNative();
        _active = null;
        // _swallowing stays on until the last finger is lifted, so a finger left on the screen does not start a pan.
    }
}
