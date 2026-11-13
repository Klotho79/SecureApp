using System.ComponentModel;
using System.Diagnostics;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Controls;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.Views;

public partial class OpicentrumBoardPage : ContentPage
{
    private readonly OpicentrumBoardViewModel _viewModel;
    private Stopwatch? _ttiStopwatch;
    private Stopwatch? _drawStopwatch;

    public OpicentrumBoardPage(OpicentrumBoardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        ApplyZoomToCorners();
        _positionsPinch = new BoardPinchZoom(PositionsBody, PositionsScroll);
        _volnoPinch = new BoardPinchZoom(VolnoBody, VolnoScroll);
        PositionsBoard.SizeChanged += (_, _) => UpdateNamesWidthCap();
        UpdateNamesWidthCap();
    }

    private readonly BoardPinchZoom _positionsPinch;
    private readonly BoardPinchZoom _volnoPinch;

    // The room-name column may widen up to 1/5 of the board's visible width; past that, pinch-zoom only makes
    // the rows taller, not the column wider (2026-11-12, user: "sloupec se saly... max do 1/5 sirky displeje").
    private void UpdateNamesWidthCap()
    {
        var maxWidth = PositionsBoard.Width > 0 ? (float)(PositionsBoard.Width / 5.0) : float.PositiveInfinity;
        PositionsBody.MaxNamesWidth = maxWidth;
        PositionsNames.MaxNamesWidth = maxWidth;
        PositionsHeader.MaxNamesWidth = maxWidth;
        ApplyZoomToCorners();
    }

    // The corner boxes hide whatever scrolls under the pinned name column and header, so they follow the zoom (132 x 22 at 100 %).
    private void ApplyZoomToCorners()
    {
        var zoom = BoardZoom.Value;
        var positionsCellsLeft = _viewModel.PositionsRows is { Count: > 0 } rows ? PositionsLayout.For(rows).CellsLeft : PositionsLayout.DefaultNameWidth + PositionsLayout.Gap;
        PositionsCorner.WidthRequest = PositionsBoardView.CappedNamesWidth(positionsCellsLeft, zoom, PositionsNames.MaxNamesWidth);
        VolnoCorner.WidthRequest = 132 * zoom;
        PositionsCorner.HeightRequest = VolnoCorner.HeightRequest = 22 * zoom;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // TTI telemetry, same pattern as LibraryPage's own _ttiStopwatch: wall-clock from the page
        // appearing to the first "Pozice" load finishing, readable via /admin/applog/{deviceId}.
        _ttiStopwatch = Stopwatch.StartNew();
        _drawStopwatch = _ttiStopwatch;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BoardZoom.Changed += ApplyZoomToCorners;
        ApplyZoomToCorners();
        // Relay pushes (2026-10-24): redraw from the freshly stored snapshot while this page is open.
        OpicentrumBoardSync.RosterPushed += OnRosterPushed;
        OpicentrumBoardSync.VolnoPushed += OnVolnoPushed;
        _viewModel.LoadCommand.Execute(null);

        // Keeps the Obnovit button's "před N min" text and its allowed/blocked colour current while the page is open.
        _refreshTimer ??= Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(15);
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
        _viewModel.NotifyRefreshState();
#if ANDROID
        // Pinch zoom on Android is read at the activity level while this page is on screen (BoardPinchHook).
        SecureApp.Presentation.Platforms.Android.BoardPinchHook.Instance.Register(_positionsPinch, PositionsBoard);
        SecureApp.Presentation.Platforms.Android.BoardPinchHook.Instance.Register(_volnoPinch, VolnoBoard);
#endif
    }

    private IDispatcherTimer? _refreshTimer;

    private void OnRefreshTimerTick(object? sender, EventArgs e) => _viewModel.NotifyRefreshState();

    private async void OnStaffingClicked(object? sender, EventArgs e)
        => await ShellNavigation.GoToAsync(nameof(OpicentrumStaffingPage));

    private void OnRosterPushed(OpicentrumRosterBoardSnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() => _viewModel.ApplyPushedPositions(snapshot));

    private void OnVolnoPushed(OpicentrumVolnoSnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() => _viewModel.ApplyPushedVolno(snapshot));

    // Sticky name column + header (2026-10-22, user's ask). Both boards are drawn on canvases (PositionsBoardView,
    // VolnoBoardView): the body scrolls natively inside the ScrollView, so the pinned name column and day header
    // are simply the overlay layers moved against the scroll offset.
    // Real time to pixels (OnAppearing to the board's first canvas draw) — "tti.opicentrum_board_page" above only
    // marks the data hand-over, which is why it read ~90 ms while the page felt like ~1 s.
    private void OnPositionsDrawn(object? sender, EventArgs e)
    {
        if (_drawStopwatch is not { } sw) return;
        _drawStopwatch = null;
        AppLog.Metric("tti.opicentrum_board_page.first_draw", sw.Elapsed.TotalMilliseconds);
    }

    private void OnPositionsScrolled(object? sender, ScrolledEventArgs e)
    {
        PositionsNames.TranslationY = -e.ScrollY;
        PositionsHeader.TranslationX = -e.ScrollX;
    }

    private void OnVolnoScrolled(object? sender, ScrolledEventArgs e)
    {
        VolnoNames.TranslationY = -e.ScrollY;
        VolnoHeader.TranslationX = -e.ScrollX;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        SecureApp.Presentation.Platforms.Android.BoardPinchHook.Instance.Clear();
#endif
        _refreshTimer?.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        BoardZoom.Changed -= ApplyZoomToCorners;
        OpicentrumBoardSync.RosterPushed -= OnRosterPushed;
        OpicentrumBoardSync.VolnoPushed -= OnVolnoPushed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The name column's width follows the workplace names, so the corner box that hides what scrolls under it follows too.
        if (e.PropertyName == nameof(OpicentrumBoardViewModel.PositionsRows)) ApplyZoomToCorners();

        if (e.PropertyName != nameof(OpicentrumBoardViewModel.IsPositionsLoading) || _viewModel.IsPositionsLoading || _ttiStopwatch is null) return;

        AppLog.Metric("tti.opicentrum_board_page", _ttiStopwatch.Elapsed.TotalMilliseconds);
        _ttiStopwatch = null; // only the first load counts as TTI
    }

    /// <summary>
    /// Two native action sheets stand in for a real drag gesture (2026-10-21) — a literal drag
    /// inside a <c>ScrollView Orientation="Both"</c> would fight the scroll view's own pan gesture,
    /// which is a much worse trade than one extra tap. First choice picks move-vs-also-assign
    /// (<c>keepSourceAssignment</c>), second picks the target workplace — both map directly onto
    /// <see cref="OpicentrumBoardViewModel.MovePersonAsync"/>'s own parameters.
    /// </summary>
    private async void OnPersonChipTapped(object? sender, OpicentrumRosterPersonChip chip)
    {
        var mode = await DisplayActionSheetAsync(chip.PersonName, "Zrušit", null, "Přesunout", "Přiřadit i na další pracoviště");
        if (mode is null or "Zrušit") return;
        var keepSourceAssignment = mode == "Přiřadit i na další pracoviště";

        var targets = _viewModel.GetTargetWorkplaceNames(chip.WorkplaceId);
        if (targets.Count == 0)
        {
            await DisplayAlertAsync(chip.PersonName, "Žádné jiné pracoviště k dispozici — zkuste nejprve Obnovit.", "OK");
            return;
        }

        var target = await DisplayActionSheetAsync("Kam přesunout?", "Zrušit", null, targets.ToArray());
        if (target is null or "Zrušit") return;

        var ok = await _viewModel.MovePersonAsync(chip, target, keepSourceAssignment);
        if (!ok)
            await DisplayAlertAsync("Nezdařilo se", _viewModel.PositionsErrorText ?? "Přesun se nezdařil.", "OK");
    }
}
