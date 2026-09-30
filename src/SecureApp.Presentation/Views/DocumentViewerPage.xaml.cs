using System.ComponentModel;
using Microsoft.Maui.Dispatching;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class DocumentViewerPage : ContentPage
{
	private readonly DocumentViewerViewModel _viewModel;

	// Pinch-to-zoom + one-finger pan (2026-09-30, user's own ask, replacing an earlier +/- button
	// attempt: "plynule zvetsovani a posun pri zvetseni... jak to byva roztazenim prstu"). This is
	// Microsoft's own documented pinch-then-pan pattern (docs: "Recognize a pinch gesture") — state
	// lives here in the Page's code-behind, not the ViewModel, because it's pure gesture/visual-
	// transform bookkeeping (Scale/TranslationX/Y on a live Image), the same "MAUI-touching glue
	// stays in the Page" split this codebase already uses for ContactsPage's drag-and-drop.
	private double _currentScale = 1;
	private double _startScale = 1;
	private double _xOffset;
	private double _yOffset;

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
		_startScale = 1;
		_xOffset = 0;
		_yOffset = 0;
		DocumentImage.Scale = 1;
		DocumentImage.TranslationX = 0;
		DocumentImage.TranslationY = 0;
	}

	private void OnDoubleTapped(object? sender, TappedEventArgs e) => ResetZoom();

	/// <summary>
	/// Verbatim structure of Microsoft's own documented pinch-to-zoom-and-pan sample (.NET MAUI docs,
	/// "Recognize a pinch gesture") — tracks the pinch's own ScaleOrigin so zoom centers on wherever
	/// the fingers actually are, not always the image's middle, and clamps the resulting translation
	/// so zoomed content can never be dragged past its own edges into empty space.
	/// </summary>
	private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
	{
		if (e.Status == GestureStatus.Started)
		{
			_startScale = DocumentImage.Scale;
			DocumentImage.AnchorX = 0;
			DocumentImage.AnchorY = 0;
		}
		if (e.Status == GestureStatus.Running)
		{
			_currentScale += (e.Scale - 1) * _startScale;
			_currentScale = Math.Max(1, _currentScale);

			var renderedX = DocumentImage.X + _xOffset;
			var deltaX = renderedX / Width;
			var deltaWidth = Width / (DocumentImage.Width * _startScale);
			var originX = (e.ScaleOrigin.X - deltaX) * deltaWidth;

			var renderedY = DocumentImage.Y + _yOffset;
			var deltaY = renderedY / Height;
			var deltaHeight = Height / (DocumentImage.Height * _startScale);
			var originY = (e.ScaleOrigin.Y - deltaY) * deltaHeight;

			var targetX = _xOffset - originX * DocumentImage.Width * (_currentScale - _startScale);
			var targetY = _yOffset - originY * DocumentImage.Height * (_currentScale - _startScale);

			targetX = Clamp(targetX, -DocumentImage.Width * (_currentScale - 1), 0);
			targetY = Clamp(targetY, -DocumentImage.Height * (_currentScale - 1), 0);

			DocumentImage.TranslationX = targetX;
			DocumentImage.TranslationY = targetY;
			DocumentImage.Scale = _currentScale;

			_xOffset = targetX;
			_yOffset = targetY;
		}
		if (e.Status is GestureStatus.Completed or GestureStatus.Canceled)
		{
			_startScale = 1; // next pinch's Scale delta is relative to THIS gesture's own start, not the very first one
		}
	}

	/// <summary>
	/// One-finger drag to keep panning after the pinch itself ends (a real pinch always needs two
	/// fingers; without this, releasing to one finger would freeze the pan). No-op while unzoomed —
	/// nothing to pan when the image already fits the screen.
	/// </summary>
	private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (DocumentImage.Scale <= 1) return;

		switch (e.StatusType)
		{
			case GestureStatus.Running:
				var targetX = Clamp(_xOffset + e.TotalX, -DocumentImage.Width * (DocumentImage.Scale - 1), 0);
				var targetY = Clamp(_yOffset + e.TotalY, -DocumentImage.Height * (DocumentImage.Scale - 1), 0);
				DocumentImage.TranslationX = targetX;
				DocumentImage.TranslationY = targetY;
				break;
			case GestureStatus.Completed:
				_xOffset = DocumentImage.TranslationX;
				_yOffset = DocumentImage.TranslationY;
				break;
		}
	}

	private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

	/// <summary>2026-09-30, user's own ask — rename this document from the viewer itself.</summary>
	private async void OnRenameClicked(object? sender, EventArgs e)
	{
		var newName = await DisplayPromptAsync("Přejmenovat dokument", "Nový název:", initialValue: _viewModel.Title);
		if (string.IsNullOrWhiteSpace(newName)) return;

		await _viewModel.RenameDocumentCommand.ExecuteAsync(newName);
	}
}
