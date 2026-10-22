using System.ComponentModel;
using System.Diagnostics;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class OpicentrumBoardPage : ContentPage
{
    private readonly OpicentrumBoardViewModel _viewModel;
    private Stopwatch? _ttiStopwatch;

    /// <summary>Set by <see cref="NotificationsPage.OnOpicentrumBoardClicked"/> right before navigating here, so TTI can cover the click itself, not just this page's own OnAppearing-onward work.</summary>
    public static Stopwatch? ClickStopwatch;

    public OpicentrumBoardPage(OpicentrumBoardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // TTI telemetry (2026-10-22, user's own ask: "proměř na S23+ proč trvá zobrazení rozpisu
        // pracovišť", then "změř od kliknutí na otevřít po zobrazení") — same pattern as
        // LibraryPage's own _ttiStopwatch, but now chained onto ClickStopwatch (started at the
        // button tap, before Shell.GoToAsync) when available, so TTI covers navigation + page/VM
        // construction too, not just this OnAppearing-onward slice. Still the FULL wall-clock gap
        // including native XAML measure/layout of the board — if tti is much bigger than
        // opicentrum.positions.load_total, the gap is the (non-virtualized, BindableLayout-based)
        // board's own layout pass, not the data fetch; if click_to_appearing itself is large, the
        // gap is navigation/page construction, before any data loading even starts.
        var clickSw = ClickStopwatch;
        ClickStopwatch = null;
        if (clickSw is not null)
            AppLog.Metric("opicentrum.positions.click_to_appearing", clickSw.Elapsed.TotalMilliseconds);
        _ttiStopwatch = clickSw ?? Stopwatch.StartNew();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        StartUiStallMonitor();
        _viewModel.LoadCommand.Execute(null);
    }

    // Sticky name column + header (2026-10-22, user's ask): the board is ONE ScrollView(Both) so
    // row heights stay naturally in sync with the names; on scroll the name labels are translated
    // by ScrollX and the header by ScrollY, which keeps them pinned while the rest pans.
    private sealed class StickyBoard
    {
        public readonly List<View> Names = [];
        public double X;
    }

    private readonly StickyBoard _positionsSticky = new();
    private readonly StickyBoard _volnoSticky = new();

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
    private void OnVolnoNameLoaded(object? sender, EventArgs e) => RegisterName(_volnoSticky, sender);
    private void OnVolnoNameUnloaded(object? sender, EventArgs e) => UnregisterName(_volnoSticky, sender);
    private void OnVolnoScrolled(object? sender, ScrolledEventArgs e) => ApplySticky(_volnoSticky, e, VolnoHeader, VolnoCorner);

    // Temporary profiling: a 50 ms UI-thread heartbeat for 10 s after open. A big gap between ticks
    // means the UI thread was frozen (nothing can paint), whatever the C# stopwatches say.
    private void StartUiStallMonitor()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        var sw = Stopwatch.StartNew();
        var last = 0.0;
        var maxGap = 0.0;
        var stalls = 0;
        timer.Tick += (_, _) =>
        {
            var now = sw.Elapsed.TotalMilliseconds;
            var gap = now - last;
            last = now;
            if (gap > maxGap) maxGap = gap;
            if (gap > 300) stalls++;
            if (now >= 10_000)
            {
                timer.Stop();
                AppLog.Metric("opicentrum.ui_stall", maxGap, "ms", ("stallsOver300ms", stalls));
            }
        };
        timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OpicentrumBoardViewModel.IsPositionsLoading) || _viewModel.IsPositionsLoading || _ttiStopwatch is null) return;

        var sw = _ttiStopwatch;
        AppLog.Metric("tti.opicentrum_board_page", sw.Elapsed.TotalMilliseconds);
        _ttiStopwatch = null; // only the first load counts as TTI

        // 2026-10-22 (user's own ask: "najdi proč to tak trvá, jestli vykreslení nebo přímo dat") —
        // the metric above stops the instant this PropertyChanged handler runs, which is BEFORE the
        // native platform actually measures/arranges/paints the newly bound board: BindableLayout
        // builds its child VisualElements synchronously here, but the real native layout+draw pass
        // is scheduled on the platform's own next frame(s), not synchronous with this call — so it
        // can under-count real "pixels on screen" time for a big (29 rows x 5 days, non-virtualized)
        // board. Two chained MainThread hops land after at least one real frame has been processed,
        // giving a second number to tell "rendering" apart from "data/apply" with.
        MainThread.BeginInvokeOnMainThread(() => MainThread.BeginInvokeOnMainThread(() =>
            AppLog.Metric("tti.opicentrum_board_page.post_layout", sw.Elapsed.TotalMilliseconds)));
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
