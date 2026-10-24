using System.ComponentModel;
using System.Diagnostics;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.Views;

public partial class OpicentrumBoardPage : ContentPage
{
    private readonly OpicentrumBoardViewModel _viewModel;
    private Stopwatch? _ttiStopwatch;

    public OpicentrumBoardPage(OpicentrumBoardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // TTI telemetry, same pattern as LibraryPage's own _ttiStopwatch: wall-clock from the page
        // appearing to the first "Pozice" load finishing, readable via /admin/applog/{deviceId}.
        _ttiStopwatch = Stopwatch.StartNew();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // Relay pushes (2026-10-24): redraw from the freshly stored snapshot while this page is open.
        OpicentrumBoardSync.RosterPushed += OnRosterPushed;
        OpicentrumBoardSync.VolnoPushed += OnVolnoPushed;
        _viewModel.LoadCommand.Execute(null);
    }

    private void OnRosterPushed(OpicentrumRosterBoardSnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() => _viewModel.ApplyPushedPositions(snapshot));

    private void OnVolnoPushed(OpicentrumVolnoSnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() => _viewModel.ApplyPushedVolno(snapshot));

    // Sticky name column + header (2026-10-22, user's ask): the board is ONE ScrollView(Both) so
    // row heights stay naturally in sync with the names; on scroll the name labels are translated
    // by ScrollX and the header by ScrollY, which keeps them pinned while the rest pans.
    private sealed class StickyBoard
    {
        public readonly List<View> Names = [];
        public double X;
    }

    private readonly StickyBoard _positionsSticky = new();

    private static void RegisterName(StickyBoard board, object? sender)
    {
        if (sender is not View v) return;
        board.Names.Add(v);
        v.TranslationX = board.X;
    }

    private static void UnregisterName(StickyBoard board, object? sender)
    {
        if (sender is View v) board.Names.Remove(v);
    }

    private static void ApplySticky(StickyBoard board, ScrolledEventArgs e, View header, View corner)
    {
        board.X = Math.Max(0, e.ScrollX);
        foreach (var v in board.Names) v.TranslationX = board.X;
        header.TranslationY = Math.Max(0, e.ScrollY);
        corner.TranslationX = board.X;
    }

    private void OnPositionsNameLoaded(object? sender, EventArgs e) => RegisterName(_positionsSticky, sender);
    private void OnPositionsNameUnloaded(object? sender, EventArgs e) => UnregisterName(_positionsSticky, sender);
    private void OnPositionsScrolled(object? sender, ScrolledEventArgs e) => ApplySticky(_positionsSticky, e, PositionsHeader, PositionsCorner);

    // Volno is drawn on canvases (VolnoBoardView): the body scrolls natively, so the pinned name column and
    // day header are simply the overlay layers moved against the scroll offset.
    private void OnVolnoScrolled(object? sender, ScrolledEventArgs e)
    {
        VolnoNames.TranslationY = -e.ScrollY;
        VolnoHeader.TranslationX = -e.ScrollX;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        OpicentrumBoardSync.RosterPushed -= OnRosterPushed;
        OpicentrumBoardSync.VolnoPushed -= OnVolnoPushed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
    private async void OnPersonChipTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject element || element.BindingContext is not OpicentrumRosterPersonChip chip)
            return;

        var mode = await DisplayActionSheet(chip.PersonName, "Zrušit", null, "Přesunout", "Přiřadit i na další pracoviště");
        if (mode is null or "Zrušit") return;
        var keepSourceAssignment = mode == "Přiřadit i na další pracoviště";

        var targets = _viewModel.GetTargetWorkplaceNames(chip.WorkplaceId);
        if (targets.Count == 0)
        {
            await DisplayAlert(chip.PersonName, "Žádné jiné pracoviště k dispozici — zkuste nejprve Obnovit.", "OK");
            return;
        }

        var target = await DisplayActionSheet("Kam přesunout?", "Zrušit", null, targets.ToArray());
        if (target is null or "Zrušit") return;

        var ok = await _viewModel.MovePersonAsync(chip, target, keepSourceAssignment);
        if (!ok)
            await DisplayAlert("Nezdařilo se", _viewModel.PositionsErrorText ?? "Přesun se nezdařil.", "OK");
    }
}
