using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;
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

    /// <summary>Mirrors <see cref="IsMonthView"/> so XAML never needs an inverse-boolean converter (this codebase's established convention — see <c>NotificationsViewModel.HasNoX</c>'s own remarks).</summary>
    public bool IsWeekView => !IsMonthView;

    private DateOnly _weekStart;
    private DateOnly _monthAnchor;

    /// <summary>Raised so the Page pushes the add/edit form for a given date (+ existing assignment id, if any) — same MAUI-free-ViewModel split this codebase already established elsewhere.</summary>
    public event Action<DateOnly, Guid?>? RequestOpenDay;

    public WorkplaceViewModel(IWorkAssignmentRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        WeekLabel = string.Empty;
        WeekDays = [];
        MonthLabel = string.Empty;
        MonthDays = [];
        _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
        var today = DateOnly.FromDateTime(DateTime.Today);
        _monthAnchor = new DateOnly(today.Year, today.Month, 1);
    }

    partial void OnIsMonthViewChanged(bool value) => OnPropertyChanged(nameof(IsWeekView));

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
        IsLoading = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var weekEnd = _weekStart.AddDays(6);

            // One range query covers both the "Dnes" card and the week strip whenever today falls
            // inside the currently-viewed week; a second, separate query only runs when it doesn't
            // (the user paged to a different week) — the "Dnes" card must stay accurate regardless
            // of which week is being browsed.
            var rangeStart = today < _weekStart ? today : _weekStart;
            var rangeEnd = today > weekEnd ? today : weekEnd;
            var assignments = await _repository.GetByDateRangeAsync(rangeStart, rangeEnd);
            var byDate = assignments.ToDictionary(a => a.Date);

            Today = ToItem(today, byDate.GetValueOrDefault(today));
            WeekDays = new ObservableCollection<AssignmentDayItem>(
                Enumerable.Range(0, 7).Select(offset =>
                {
                    var date = _weekStart.AddDays(offset);
                    return ToItem(date, byDate.GetValueOrDefault(date));
                }));
            WeekLabel = FormatWeekLabel(_weekStart, weekEnd);
        }
        finally
        {
            IsLoading = false;
        }

        if (IsMonthView)
            await LoadMonthAsync();
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        _weekStart = _weekStart.AddDays(-7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        _weekStart = _weekStart.AddDays(7);
        await LoadAsync();
    }

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
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ShowMonthViewAsync()
    {
        if (IsMonthView) return;
        IsMonthView = true;
        await LoadMonthAsync();
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        _monthAnchor = _monthAnchor.AddMonths(-1);
        await LoadMonthAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        _monthAnchor = _monthAnchor.AddMonths(1);
        await LoadMonthAsync();
    }

    /// <summary>
    /// Always a full 6-row (42-cell) grid starting on the Monday on/before the 1st — a fixed height
    /// keeps the grid from visually jumping between 4/5/6-row months as the user pages through them.
    /// Cells outside <see cref="_monthAnchor"/>'s own month are still real, tappable days (spec
    /// doesn't say otherwise, and <c>AddAssignmentPage</c> always shows its own explicit date, so
    /// there's no ambiguity) — <see cref="MonthDayCell.IsCurrentMonth"/> just dims them.
    /// </summary>
    private async Task LoadMonthAsync()
    {
        IsLoading = true;
        try
        {
            var gridStart = StartOfWeek(_monthAnchor);
            var gridEnd = gridStart.AddDays(41);
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
            assignment?.Id,
            OpenMonthDayCommand);
    }

    private AssignmentDayItem ToItem(DateOnly date, WorkAssignment? assignment)
    {
        var isToday = date == DateOnly.FromDateTime(DateTime.Today);
        var dayLabel = $"{CzechDayAbbreviations[(int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1]} {date.Day}. {date.Month}.";

        if (assignment is null)
        {
            return new AssignmentDayItem(date, dayLabel, isToday, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, OpenDayCommand);
        }

        var timeText = assignment.StartTime is { } start && assignment.EndTime is { } end
            ? $"{start:HH:mm}–{end:HH:mm}"
            : string.Empty;

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
            assignment.Id,
            OpenDayCommand);
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
/// One day, in either the "Dnes" card or a Week-strip row — <see cref="TypeText"/> is the enum name
/// (e.g. "Vacation"), bound against XAML DataTrigger Value comparisons for the type accent color,
/// this codebase's established "no converters" convention (see <c>NotificationsPage.xaml</c>'s own
/// priority accent bar for the same pattern). <see cref="TypeLabel"/>/<see cref="Glyph"/> are the
/// precomputed Czech display text.
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
    Guid? AssignmentId,
    ICommand OpenCommand)
{
    public bool HasWorkplace => !string.IsNullOrEmpty(WorkplaceText);
    public bool HasTime => !string.IsNullOrEmpty(TimeText);
    public bool HasNoAssignment => !HasAssignment;
    public string DisplayTypeLabel => HasAssignment ? TypeLabel : "Bez záznamu";
}

/// <summary>One cell in the Month grid — deliberately minimal (just a day number + a color dot) compared to <see cref="AssignmentDayItem"/>'s full row, since a month grid has to fit 42 cells on one screen. <see cref="TypeText"/> follows the same "no converters" DataTrigger convention as everywhere else on this page.</summary>
public sealed record MonthDayCell(
    DateOnly Date,
    string DayNumberText,
    bool IsCurrentMonth,
    bool IsToday,
    bool HasAssignment,
    string TypeText,
    Guid? AssignmentId,
    ICommand OpenCommand);
