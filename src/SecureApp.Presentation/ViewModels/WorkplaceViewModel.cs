using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// WORKPLACE + CALENDAR (NOTIFICATION_HUB_SPEC.md Phase 5) — one page covering both spec sections: a
/// "Dnes" card (spec §6's TODAY block) plus a Week/Month toggle (spec §7 — "Day / Week / Month views;
/// Week is primary"). Day itself is covered by <c>AddAssignmentPage</c>, the day-detail/editor tapped
/// into from either view, rather than a third top-level mode — see NOTIFICATION_HUB_SPEC.md's Status.
///
/// <see cref="WorkAssignment"/> is local per-device (this device's own person's schedule) — see that
/// entity's own remarks; only the <c>Workplace</c> NAME catalog it references is relay-synced.
/// </summary>
public sealed partial class WorkplaceViewModel : ObservableObject
{
    private static readonly string[] CzechDayAbbreviations = ["Po", "Út", "St", "Čt", "Pá", "So", "Ne"];
    private static readonly string[] CzechMonthGenitive =
    [
        "ledna", "února", "března", "dubna", "května", "června",
        "července", "srpna", "září", "října", "listopadu", "prosince"
    ];
    private static readonly string[] CzechMonthNominative =
    [
        "Leden", "Únor", "Březen", "Duben", "Květen", "Červen",
        "Červenec", "Srpen", "Září", "Říjen", "Listopad", "Prosinec"
    ];

    private readonly IWorkAssignmentRepository _repository;
    private readonly IOpicentrumSyncService _opicentrumSyncService;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial AssignmentDayItem? Today { get; set; }

    [ObservableProperty]
    public partial string WeekLabel { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<AssignmentDayItem> WeekDays { get; set; }

    [ObservableProperty]
    public partial bool IsMonthView { get; set; }

    [ObservableProperty]
    public partial string MonthLabel { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<MonthDayCell> MonthDays { get; set; }

    /// <summary>Surfaces what the last Opicentrum sync actually did (2026-09-21, user reported "nevidím dovolené" with no way to tell whether that was a real bug or just nothing to import — this is what made the actual bug findable) — null while nothing's been synced yet or credentials aren't configured.</summary>
    [ObservableProperty]
    public partial string? SyncStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasSyncStatus { get; set; }

    /// <summary>Collapsed by default (2026-09-21, user's own ask: "sbalitelná/rozklikávací" over an always-visible row) — toggled by a small ℹ button next to the Týden/Měsíc chips.</summary>
    [ObservableProperty]
    public partial bool IsLegendExpanded { get; set; }

    /// <summary>One row per <see cref="AssignmentType"/>, built fresh on every <see cref="LoadAsync"/> so a color changed on the Settings page (per-device, see <c>AssignmentColorCatalog</c>) shows up the next time this page appears — same "just re-render, no extra invalidation wiring" pattern the rest of this ViewModel already uses.</summary>
    [ObservableProperty]
    public partial ObservableCollection<AssignmentLegendItem> LegendItems { get; set; }

    /// <summary>Mirrors <see cref="IsMonthView"/> so XAML never needs an inverse-boolean converter (this codebase's established convention — see <c>NotificationsViewModel.HasNoX</c>'s own remarks).</summary>
    public bool IsWeekView => !IsMonthView;

    private DateOnly _weekStart;
    private DateOnly _monthAnchor;

    /// <summary>Raised so the Page pushes the add/edit form for a given date (+ existing assignment id, if any) — same MAUI-free-ViewModel split this codebase already established elsewhere.</summary>
    public event Action<DateOnly, Guid?>? RequestOpenDay;

    public WorkplaceViewModel(IWorkAssignmentRepository repository, IOpicentrumSyncService opicentrumSyncService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _opicentrumSyncService = opicentrumSyncService ?? throw new ArgumentNullException(nameof(opicentrumSyncService));
        WeekLabel = string.Empty;
        WeekDays = [];
        MonthLabel = string.Empty;
        MonthDays = [];
        LegendItems = new ObservableCollection<AssignmentLegendItem>(BuildLegendItems());
        _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
        var today = DateOnly.FromDateTime(DateTime.Today);
        _monthAnchor = new DateOnly(today.Year, today.Month, 1);
    }

    partial void OnIsMonthViewChanged(bool value) => OnPropertyChanged(nameof(IsWeekView));
    partial void OnSyncStatusTextChanged(string? value) => HasSyncStatus = !string.IsNullOrEmpty(value);

    /// <summary>
    /// Called from the Page's own OnAppearing — including every time it re-appears after
    /// AddAssignmentPage pops back (a save/delete there must be reflected here immediately). Always
    /// refreshes the Dnes card + Week (both share one range query), and additionally the Month grid
    /// when that's the currently-visible view — otherwise a day edited from Month view would show
    /// stale data until the user manually toggled away and back.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        LegendItems = new ObservableCollection<AssignmentLegendItem>(BuildLegendItems());
        await RenderWeekLocalAsync();
        if (IsMonthView)
            await RenderMonthLocalAsync();

        // One sync call covering the union of both ranges, not two independent concurrent ones — two
        // near-simultaneous logins to the same Opicentrum account collide server-side (confirmed
        // 2026-09-22 live: one login's session silently invalidates the other's, so whichever call's
        // own welcome-check GET lands second sees itself logged out and reports "login failed", even
        // though the other one succeeded) — see SyncAsync's own remarks on why every request needs the
        // browser User-Agent, a related but separate finding from the same debugging session.
        var weekRange = CurrentWeekSyncRange();
        if (IsMonthView)
        {
            var monthRange = CurrentMonthSyncRange();
            var rangeStart = weekRange.Start < monthRange.Start ? weekRange.Start : monthRange.Start;
            var rangeEnd = weekRange.End > monthRange.End ? weekRange.End : monthRange.End;
            _ = SyncInBackgroundAsync(rangeStart, rangeEnd, async () =>
            {
                await RenderWeekLocalAsync();
                await RenderMonthLocalAsync();
            });
        }
        else
        {
            _ = SyncInBackgroundAsync(weekRange.Start, weekRange.End, RenderWeekLocalAsync);
        }
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        _weekStart = _weekStart.AddDays(-7);
        await RenderWeekLocalAsync();
        var range = CurrentWeekSyncRange();
        _ = SyncInBackgroundAsync(range.Start, range.End, RenderWeekLocalAsync);
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        _weekStart = _weekStart.AddDays(7);
        await RenderWeekLocalAsync();
        var range = CurrentWeekSyncRange();
        _ = SyncInBackgroundAsync(range.Start, range.End, RenderWeekLocalAsync);
    }

    [RelayCommand]
    private void ToggleLegend() => IsLegendExpanded = !IsLegendExpanded;

    [RelayCommand]
    private void OpenDay(AssignmentDayItem? item)
    {
        if (item is null) return;
        RequestOpenDay?.Invoke(item.Date, item.AssignmentId);
    }

    [RelayCommand]
    private void OpenMonthDay(MonthDayCell? cell)
    {
        if (cell is null) return;
        RequestOpenDay?.Invoke(cell.Date, cell.AssignmentId);
    }

    [RelayCommand]
    private async Task ShowWeekViewAsync()
    {
        if (!IsMonthView) return;
        IsMonthView = false;
        // No fresh sync here on purpose — Month's own sync just covered the overlapping dates
        // moments ago; switching views is purely a local re-render.
        await RenderWeekLocalAsync();
    }

    [RelayCommand]
    private async Task ShowMonthViewAsync()
    {
        if (IsMonthView) return;
        IsMonthView = true;
        await RenderMonthLocalAsync();
        var range = CurrentMonthSyncRange();
        _ = SyncInBackgroundAsync(range.Start, range.End, RenderMonthLocalAsync);
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        _monthAnchor = _monthAnchor.AddMonths(-1);
        await RenderMonthLocalAsync();
        var range = CurrentMonthSyncRange();
        _ = SyncInBackgroundAsync(range.Start, range.End, RenderMonthLocalAsync);
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        _monthAnchor = _monthAnchor.AddMonths(1);
        await RenderMonthLocalAsync();
        var range = CurrentMonthSyncRange();
        _ = SyncInBackgroundAsync(range.Start, range.End, RenderMonthLocalAsync);
    }

    /// <summary>
    /// One range query covers both the "Dnes" card and the week strip whenever today falls inside the
    /// currently-viewed week; a second, separate bound only kicks in when it doesn't (the user paged to
    /// a different week) — the "Dnes" card must stay accurate regardless of which week is being browsed.
    /// Local-only (no network) — see <see cref="SyncInBackgroundAsync"/> for the Opicentrum half.
    /// </summary>
    private (DateOnly Start, DateOnly End) CurrentWeekSyncRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekEnd = _weekStart.AddDays(6);
        var rangeStart = today < _weekStart ? today : _weekStart;
        var rangeEnd = today > weekEnd ? today : weekEnd;
        return (rangeStart, rangeEnd);
    }

    /// <summary>Always a full 6-row (42-cell) grid starting on the Monday on/before the 1st — see <see cref="RenderMonthLocalAsync"/>'s own remarks for why.</summary>
    private (DateOnly Start, DateOnly End) CurrentMonthSyncRange()
    {
        var gridStart = StartOfWeek(_monthAnchor);
        return (gridStart, gridStart.AddDays(41));
    }

    /// <summary>Renders Dnes + Week strip purely from the local repository — no network. Used both for the initial paint and to re-render once <see cref="SyncInBackgroundAsync"/> has written fresh data.</summary>
    private async Task RenderWeekLocalAsync()
    {
        IsLoading = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var (rangeStart, rangeEnd) = CurrentWeekSyncRange();

            var assignments = await _repository.GetByDateRangeAsync(rangeStart, rangeEnd);
            var byDate = assignments.ToDictionary(a => a.Date);

            Today = ToItem(today, byDate.GetValueOrDefault(today), AssignmentColorCatalog.NoAssignmentCardColor);
            WeekDays = new ObservableCollection<AssignmentDayItem>(
                Enumerable.Range(0, 7).Select(offset =>
                {
                    var date = _weekStart.AddDays(offset);
                    return ToItem(date, byDate.GetValueOrDefault(date), AssignmentColorCatalog.NoAssignmentSurfaceColor);
                }));
            WeekLabel = FormatWeekLabel(_weekStart, _weekStart.AddDays(6));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Renders the Month grid purely from the local repository — no network. A fixed height keeps the
    /// grid from visually jumping between 4/5/6-row months as the user pages through them. Cells
    /// outside <see cref="_monthAnchor"/>'s own month are still real, tappable days (spec doesn't say
    /// otherwise, and <c>AddAssignmentPage</c> always shows its own explicit date, so there's no
    /// ambiguity) — <see cref="MonthDayCell.IsCurrentMonth"/> just dims them.
    /// </summary>
    private async Task RenderMonthLocalAsync()
    {
        IsLoading = true;
        try
        {
            var (gridStart, gridEnd) = CurrentMonthSyncRange();

            var assignments = await _repository.GetByDateRangeAsync(gridStart, gridEnd);
            var byDate = assignments.ToDictionary(a => a.Date);

            MonthDays = new ObservableCollection<MonthDayCell>(
                Enumerable.Range(0, 42).Select(offset =>
                {
                    var date = gridStart.AddDays(offset);
                    return ToMonthCell(date, byDate.GetValueOrDefault(date));
                }));
            MonthLabel = $"{CzechMonthNominative[_monthAnchor.Month - 1]} {_monthAnchor.Year}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opicentrum sync (2026-09-21), run in the background rather than blocking the local render —
    /// best-effort: a network/login failure must never prevent the already-rendered local Rozpis from
    /// staying usable (OpicentrumSyncService itself publishes a Notification on failure — see its own
    /// remarks). <paramref name="onSynced"/> re-renders from the local repository afterwards so newly
    /// written assignments actually show up without the user having to manually refresh.
    /// </summary>
    private async Task SyncInBackgroundAsync(DateOnly rangeStart, DateOnly rangeEnd, Func<Task> onSynced)
    {
        try { SyncStatusText = DescribeSyncResult(await _opicentrumSyncService.SyncAsync(rangeStart, rangeEnd)); }
        catch (Exception ex) { SyncStatusText = $"Synchronizace s Opicentrem selhala: {ex.Message}"; }
        await onSynced();
    }

    private MonthDayCell ToMonthCell(DateOnly date, WorkAssignment? assignment)
    {
        var isToday = date == DateOnly.FromDateTime(DateTime.Today);
        var isCurrentMonth = date.Month == _monthAnchor.Month && date.Year == _monthAnchor.Year;
        return new MonthDayCell(
            date,
            date.Day.ToString(CultureInfo.InvariantCulture),
            isCurrentMonth,
            isToday,
            assignment is not null,
            assignment?.Type.ToString() ?? string.Empty,
            assignment is not null ? AssignmentColorCatalog.SoftColor(assignment.Type) : AssignmentColorCatalog.NoAssignmentSurfaceColor,
            !string.IsNullOrEmpty(assignment?.OnCallWorkplaceName),
            assignment?.Id,
            OpenMonthDayCommand);
    }

    private AssignmentDayItem ToItem(DateOnly date, WorkAssignment? assignment, Color noAssignmentColor)
    {
        var isToday = date == DateOnly.FromDateTime(DateTime.Today);
        var dayLabel = $"{CzechDayAbbreviations[(int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1]} {date.Day}. {date.Month}.";

        if (assignment is null)
        {
            return new AssignmentDayItem(date, dayLabel, isToday, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, noAssignmentColor, noAssignmentColor, null, OpenDayCommand);
        }

        var timeText = assignment.StartTime is { } start && assignment.EndTime is { } end
            ? $"{start:HH:mm}–{end:HH:mm}"
            : string.Empty;

        // OnCallText (2026-09-21) — a duty overlaid on top of this day's own primary type; see
        // WorkAssignment.OnCallWorkplaceName's own remarks for how the sync decides when this is set.
        var onCallText = string.IsNullOrEmpty(assignment.OnCallWorkplaceName)
            ? string.Empty
            : $"{AssignmentTypeCatalog.Glyph(AssignmentType.OnCall)} {AssignmentTypeCatalog.Label(AssignmentType.OnCall)}: {assignment.OnCallWorkplaceName}";

        return new AssignmentDayItem(
            date,
            dayLabel,
            isToday,
            true,
            assignment.Type.ToString(),
            AssignmentTypeCatalog.Label(assignment.Type),
            AssignmentTypeCatalog.Glyph(assignment.Type),
            assignment.WorkplaceName ?? string.Empty,
            timeText,
            AssignmentColorCatalog.SoftColor(assignment.Type),
            AssignmentColorCatalog.BaseColor(assignment.Type),
            assignment.Id,
            OpenDayCommand,
            onCallText);
    }

    private static IEnumerable<AssignmentLegendItem> BuildLegendItems() =>
        AssignmentTypeCatalog.All.Select(type => new AssignmentLegendItem(
            AssignmentColorCatalog.BaseColor(type),
            AssignmentTypeCatalog.Glyph(type),
            AssignmentTypeCatalog.Label(type)));

    private static string? DescribeSyncResult(OpicentrumSyncResult result)
    {
        // Reference equality on purpose (2026-09-21 bug fix) — OpicentrumSyncResult is a record, and
        // NotConfigured's field values (Success=true, 0, 0, null) are indistinguishable by value from a
        // genuine "synced fine, nothing changed" result, so `==` silently swallowed the status text on
        // every no-op sync. NotConfigured is only ever returned as this exact static instance.
        if (ReferenceEquals(result, OpicentrumSyncResult.NotConfigured)) return null; // not set up yet — nothing to report
        if (!result.Success) return $"Synchronizace s Opicentrem selhala: {result.ErrorMessage}";
        return result.CreatedCount == 0 && result.UpdatedCount == 0
            ? "Synchronizováno s Opicentrem — beze změn."
            : $"Synchronizováno s Opicentrem — nové: {result.CreatedCount}, aktualizované: {result.UpdatedCount}.";
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1);
        return date.AddDays(-diff);
    }

    private static string FormatWeekLabel(DateOnly start, DateOnly end)
    {
        if (start.Month == end.Month)
            return $"{start.Day}.–{end.Day}. {CzechMonthGenitive[end.Month - 1]} {end.Year}";
        return $"{start.Day}. {CzechMonthGenitive[start.Month - 1]} – {end.Day}. {CzechMonthGenitive[end.Month - 1]} {end.Year}";
    }
}

/// <summary>
/// One day, in either the "Dnes" card or a Week-strip row. <see cref="TypeText"/> is kept only for the
/// "Dnes" card, which still uses it for icon-independent things elsewhere — the color itself no longer
/// goes through XAML DataTrigger/StaticResource matching (2026-09-21: replaced by <see cref="SoftColor"/>/
/// <see cref="AccentColor"/>, precomputed here from the per-device-customizable <c>AssignmentColorCatalog</c>,
/// bound directly instead — a DataTrigger per type couldn't react to a user-chosen color without either
/// a converter or 8 more triggers per color, and this codebase already precomputes display text the same
/// way for <see cref="TypeLabel"/>/<see cref="Glyph"/>). <c>Colors.Transparent</c> when
/// <see cref="HasAssignment"/> is false, so the binding needs no separate IsVisible/converter to suppress
/// the tint on an empty day.
/// </summary>
public sealed record AssignmentDayItem(
    DateOnly Date,
    string DayLabel,
    bool IsToday,
    bool HasAssignment,
    string TypeText,
    string TypeLabel,
    string Glyph,
    string WorkplaceText,
    string TimeText,
    Color SoftColor,
    Color AccentColor,
    Guid? AssignmentId,
    ICommand OpenCommand,
    string OnCallText = "")
{
    public bool HasWorkplace => !string.IsNullOrEmpty(WorkplaceText);
    public bool HasTime => !string.IsNullOrEmpty(TimeText);
    public bool HasNoAssignment => !HasAssignment;

    /// <summary>
    /// 2026-09-21, user's own ask: "vyhoď zbytečný nápis Práce v kalendáři... odlišíme to jen barvou" —
    /// Práce specifically (the most common type) drops its text label and relies on color/the accent
    /// dot alone. 2026-09-22 extended the same suppression to ANY type once <see cref="WorkplaceText"/>
    /// is set (not just Work) — the user's own ask ("dej to alespoň na jeden řádek") after a PS/VV/NV
    /// leave day started showing both the generic type label ("Volno") AND the raw code ("PS") on two
    /// separate lines; a type whose <see cref="WorkplaceText"/> is already self-descriptive (the raw
    /// leave code, or <c>AssignmentType.Other</c>'s own full sentence — see
    /// <c>OpicentrumSyncService.MergeSpravavolnaAsync</c>'s own remarks) doesn't need the generic label
    /// repeated above it. Empty (not "Bez záznamu") for a Work day, so <see cref="HasDisplayTypeLabel"/>
    /// can hide the Label outright rather than rendering a blank line.
    /// </summary>
    public string DisplayTypeLabel => HasNoAssignment ? "Bez záznamu" : (TypeText == "Work" || HasWorkplace) ? string.Empty : TypeLabel;
    public bool HasDisplayTypeLabel => !string.IsNullOrEmpty(DisplayTypeLabel);

    /// <summary>2026-09-21 — a duty overlaid on top of this day's own primary type (see <c>WorkAssignment.OnCallWorkplaceName</c>'s own remarks); e.g. a normal shift followed later the same day by on-call.</summary>
    public bool HasOnCall => !string.IsNullOrEmpty(OnCallText);
}

/// <summary>One cell in the Month grid — deliberately minimal (just a day number + a tinted background) compared to <see cref="AssignmentDayItem"/>'s full row, since a month grid has to fit 42 cells on one screen. <see cref="SoftColor"/> replaces the old per-type DataTrigger, same reasoning as <see cref="AssignmentDayItem"/>'s own remarks.</summary>
public sealed record MonthDayCell(
    DateOnly Date,
    string DayNumberText,
    bool IsCurrentMonth,
    bool IsToday,
    bool HasAssignment,
    string TypeText,
    Color SoftColor,
    bool HasOnCall,
    Guid? AssignmentId,
    ICommand OpenCommand);

/// <summary>One row in the collapsible color legend — <see cref="Color"/> is the same <c>AssignmentColorCatalog.BaseColor</c> the Week strip's accent dot uses, so the legend always matches what's actually on screen, including after a user customizes it on the Settings page.</summary>
public sealed record AssignmentLegendItem(Color Color, string Glyph, string Label);
