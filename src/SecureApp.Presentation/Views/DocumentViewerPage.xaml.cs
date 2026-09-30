using System.ComponentModel;
using Microsoft.Maui.Dispatching;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class DocumentViewerPage : ContentPage
{
	private readonly DocumentViewerViewModel _viewModel;

	// Pinch-to-zoom + one-finger pan (2026-09-30, user's own ask, replacing an earlier +/- button
	// attempt: "plynule zvetsovani a posun pri zvetseni... jak to byva roztazenim prstu"). State
	// lives here in the Page's code-behind, not the ViewModel, because it's pure gesture/visual-
	// transform bookkeeping (Scale/TranslationX/Y on a live Image), the same "MAUI-touching glue
	// stays in the Page" split this codebase already uses for ContactsPage's drag-and-drop.
	//
	// 2026-09-30 rewrite (v1 had real bugs — user: "zvetsuje se z praveho dolniho rohu a posun je
	// pomaly"): v1 flipped AnchorX/AnchorY from the default (0.5, 0.5) to (0, 0) the moment a pinch
	// started, which snaps the existing Scale transform onto a NEW origin instantly — a visible jump
	// toward one corner — and then computed the pan clamp range for a (0,0) anchor while the actual
	// anchor briefly disagreed mid-transition, so the pannable range was wrong (too small in one
	// axis), which read as "slow"/stuck panning. This version never touches AnchorX/AnchorY at all —
	// they stay MAUI's own default (0.5, 0.5), so zooming always expands from the image's CENTER,
	// and the pan clamp is the correspondingly SYMMETRIC ± half-overflow range for that anchor
	// (previously it was the range for a top-left anchor, which does not match a center anchor).
	// Scale itself is now a plain running multiply (Scale *= e.Scale, e.Scale being MAUI's own
	// per-callback pinch delta) instead of an additive approximation — simpler and numerically
	// steadier, which should also read as smoother.
	private double _currentScale = 1;
	private double _panX;
	private double _panY;
	private bool _ignoreNextPinchDelta;
	private double _smoothedDelta = 1;

	public DocumentViewerPage(DocumentViewerViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _viewModel = viewModel;
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		// Defer the (heavy) image render until the open animation has settled, so rasterizing the
		// image doesn't jank the slide-in (2026-09-11) — the render itself also runs off the UI
		// thread now (see DocumentViewerViewModel.GoToPageAsync). Same "separate graphics from
		// loading" principle as the chat pages.
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(280), () => _viewModel.LoadDocumentCommand.Execute(null));
	}

	/// <summary>A fresh page always starts unzoomed — a leftover zoom/pan from the previous page would land the new page's content half off-screen.</summary>
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(DocumentViewerViewModel.CurrentPageImage))
			ResetZoom();
	}

	private void ResetZoom()
	{
		_currentScale = 1;
		_panX = 0;
		_panY = 0;
		_smoothedDelta = 1;
		DocumentImage.Scale = 1;
		DocumentImage.TranslationX = 0;
		DocumentImage.TranslationY = 0;
	}

	private void OnDoubleTapped(object? sender, TappedEventArgs e) => ResetZoom();

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

		// 2026-09-30 (v7, measured): on-device metrics caught raw e.Scale genuinely alternating between
		// ~3x and ~0.3x every callback while holding near max zoom — not real finger motion (two touch
		// samples ~17ms apart can't legitimately swing the pinch span 10x and back), just noisy/duplicate
		// platform touch data. Reject it outright rather than clamp-and-apply a squashed version of it.
		if (e.Scale < 0.8 || e.Scale > 1.25) return;

		// 2026-09-30 (v9, measured): v4-v8 amplified e.Scale via Math.Pow(_, 1.6) so max zoom was
		// reachable in fewer physical pinch gestures. On-device metrics then showed this was ALSO
		// amplifying completely ordinary per-frame touch jitter (e.g. raw 0.9/1.2 alternating even
		// during an otherwise steady pinch) into ±40% swings per frame — read as "skace tam a zpet"
		// (jumps back and forth). v2/v3, which tracked e.Scale directly with no amplification, were
		// never reported as jumpy, only slower to reach max zoom over 2-3 gestures — a far smaller
		// complaint. Amplification removed entirely; a light exponential smoothing of the accepted
		// delta (blend with the previous frame's) irons out the jitter that's still visible even
		// unamplified, without another guess-and-measure round.
		_smoothedDelta = _smoothedDelta * 0.5 + e.Scale * 0.5;
		_currentScale = Math.Clamp(_currentScale * _smoothedDelta, 1, MaxScale);
		DocumentImage.Scale = _currentScale;
		ClampTranslation();
		// Pinch has no "total since gesture start" value the way Pan does (see OnPanUpdated's own
		// remarks) — its own translation nudges are ad hoc, so the clamped result IS the new baseline
		// immediately, not just for display.
		_panX = DocumentImage.TranslationX;
		_panY = DocumentImage.TranslationY;
	}

	/// <summary>
	/// One-finger drag to keep panning after the pinch itself ends (a real pinch always needs two
	/// fingers; without this, releasing to one finger would freeze the pan). No-op while unzoomed —
	/// nothing to pan when the image already fits the screen.
	///
	/// <see cref="PanUpdatedEventArgs.TotalX"/>/<see cref="PanUpdatedEventArgs.TotalY"/> are the total
	/// distance panned since THIS gesture started, not since the last callback — so <see cref="_panX"/>/
	/// <see cref="_panY"/> (the position BEFORE this gesture) must stay untouched for the whole Running
	/// phase; the v1 bug ("posun je pomaly") was clamping's own result getting written back into that
	/// baseline on every single Running callback, which then got added to again on the next one —
	/// compounding into hitting the edge clamp almost immediately, which read as the drag "sticking".
	/// Only Completed bakes the final (already-clamped) position in as the next gesture's baseline.
	/// </summary>
	private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (_currentScale <= 1) return;

		switch (e.StatusType)
		{
			case GestureStatus.Running:
				DocumentImage.TranslationX = _panX + e.TotalX;
				DocumentImage.TranslationY = _panY + e.TotalY;
				ClampTranslation(); // display-only clamp — does NOT touch _panX/_panY, see remarks above
				break;
			case GestureStatus.Completed:
				_panX = DocumentImage.TranslationX;
				_panY = DocumentImage.TranslationY;
				break;
		}
	}

	/// <summary>
	/// DocumentImage keeps MAUI's own default AnchorX/AnchorY (0.5, 0.5, i.e. its center) — deliberately
	/// never touched — so Scale always expands the image symmetrically outward from its middle. The
	/// overflow past each edge is therefore Width/Height * (scale - 1) SPLIT EVENLY on both sides, so
	/// the pannable range is ± half that, not the 0..-full-overflow range a top-left anchor would need.
	/// </summary>
	private void ClampTranslation()
	{
		var maxX = DocumentImage.Width * (_currentScale - 1) / 2;
		var maxY = DocumentImage.Height * (_currentScale - 1) / 2;
		DocumentImage.TranslationX = Math.Clamp(DocumentImage.TranslationX, -maxX, maxX);
		DocumentImage.TranslationY = Math.Clamp(DocumentImage.TranslationY, -maxY, maxY);
	}

	// 2026-09-30: lowered from 4 — the user saw the image "glicovat" (glitch) at/near the old max,
	// which reads as a rendering-transform limit (stretching an already-rasterized bitmap this far
	// via Scale, not re-rendering it at higher resolution) rather than something this gesture code
	// itself can fix outright. Paired with DocumentViewerViewModel's higher source render resolution
	// (1800x2400, up from 1200x1600) so the same READABLE result needs less extreme Scale to reach.
	private const double MaxScale = 3;

	/// <summary>2026-09-30, user's own ask — rename this document from the viewer itself.</summary>
	private async void OnRenameClicked(object? sender, EventArgs e)
	{
		var newName = await DisplayPromptAsync("Přejmenovat dokument", "Nový název:", initialValue: _viewModel.Title);
		if (string.IsNullOrWhiteSpace(newName)) return;

		await _viewModel.RenameDocumentCommand.ExecuteAsync(newName);
	}
}
