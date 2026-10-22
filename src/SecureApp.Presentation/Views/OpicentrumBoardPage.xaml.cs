using System.ComponentModel;
using System.Diagnostics;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;

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
        // TTI telemetry (2026-10-22, user's own ask: "proměř na S23+ proč trvá zobrazení rozpisu
        // pracovišť") — same pattern as LibraryPage's own _ttiStopwatch. This measures the FULL
        // wall-clock gap including native XAML measure/layout of the board, not just the C#-side
        // work OpicentrumBoardViewModel.Positions.cs's own AppLog.Metric calls already time — if
        // this number is much bigger than opicentrum.positions.load_total, the gap is the
        // (non-virtualized, BindableLayout-based) board's own layout pass, not the data fetch.
        _ttiStopwatch = Stopwatch.StartNew();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
