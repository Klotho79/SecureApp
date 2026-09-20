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
/// WORKPLACE + CALENDAR (2026-09-20, NOTIFICATION_HUB_SPEC.md Phase 5, "pokracujem kalendarem") —
/// one page covering both spec sections: a "Dnes" card (spec §6's TODAY block) plus a navigable
/// Week strip (spec §7 — "Week is primary"). Day/Month calendar views are a later slice; this is the
/// first one, per the spec's own §32/§36 incremental-delivery rule.
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

    private readonly IWorkAssignmentRepository _repository;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial AssignmentDayItem? Today { get; set; }

    [ObservableProperty]
    public partial string WeekLabel { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<AssignmentDayItem> WeekDays { get; set; }

    private DateOnly _weekStart;

    /// <summary>Raised so the Page pushes the add/edit form for a given date (+ existing assignment id, if any) — same MAUI-free-ViewModel split this codebase already established elsewhere.</summary>
    public event Action<DateOnly, Guid?>? RequestOpenDay;

    public WorkplaceViewModel(IWorkAssignmentRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        WeekLabel = string.Empty;
        WeekDays = [];
        _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
    }

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
