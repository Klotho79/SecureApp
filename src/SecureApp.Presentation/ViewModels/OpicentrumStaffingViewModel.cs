using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Admin's settings for the position suggestions (2026-10-25, POZICE_NAVRH_PLAN.md phase 1): every doctor's grade,
/// section, specialisations and target shares per workplace, every workplace's rules, and which workplace each duty
/// slot puts its doctor on. Stored as one <see cref="OpicentrumStaffingConfig"/> on the relay (Admin writes).
/// The lists of people, workplaces and duty slots come from what this device already has from Opicentrum (the
/// cached Pozice weeks, Volno, the duty roster); "Načíst z Opicentra" scrapes the last weeks for anyone missing.
/// </summary>
public sealed partial class OpicentrumStaffingViewModel : ObservableObject
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    /// <summary>NEZAŘAZENÍ is a row on the board, not a position anyone is suggested for.</summary>
    private const int UnassignedWorkplaceId = 999;

    /// <summary>How many past weeks "Načíst z Opicentra" scrapes (one login + one page each).</summary>
    private const int ScrapedPastWeeks = 4;

    /// <summary>A doctor this many percentage points or more from fair is highlighted in the statistics.</summary>
    private const double UnfairDeviationPoints = 15;

    private readonly IOpicentrumStaffingConfigService _configService;
    private readonly IOpicentrumSyncService _opicentrumSyncService;
    private readonly OpicentrumHistoryService _historyService;
    private readonly ICurrentUserService _currentUserService;
    private readonly RolePreviewService _rolePreviewService;

    private OpicentrumStaffingConfig _config = OpicentrumStaffingConfig.Empty;
    private readonly Dictionary<int, string> _knownPeople = [];
    private readonly Dictionary<int, string> _knownWorkplaces = [];
    private readonly HashSet<string> _knownDutySlots = new(StringComparer.OrdinalIgnoreCase);

    public OpicentrumStaffingViewModel(
        IOpicentrumStaffingConfigService configService,
        IOpicentrumSyncService opicentrumSyncService,
        OpicentrumHistoryService historyService,
        ICurrentUserService currentUserService,
        RolePreviewService rolePreviewService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _opicentrumSyncService = opicentrumSyncService ?? throw new ArgumentNullException(nameof(opicentrumSyncService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _rolePreviewService = rolePreviewService ?? throw new ArgumentNullException(nameof(rolePreviewService));
    }

    public IReadOnlyList<GradeOption> GradeOptions => GradeOption.All;

    /// <summary>"—" plus every workplace — the section picker and the duty-slot pickers.</summary>
    public ObservableCollection<WorkplaceOption> WorkplaceOptions { get; } = [];

    public ObservableCollection<StaffProfileItem> People { get; } = [];
    public ObservableCollection<StaffProfileItem> FilteredPeople { get; } = [];
    public ObservableCollection<WorkplaceProfileItem> Workplaces { get; } = [];
    public ObservableCollection<DutySlotItem> DutySlots { get; } = [];

    [ObservableProperty]
    public partial int SubTab { get; set; }

    public bool IsPeopleSubTab => SubTab == 0;
    public bool IsWorkplacesSubTab => SubTab == 1;
    public bool IsDutySubTab => SubTab == 2;
    public bool IsStatsSubTab => SubTab == 3;

    partial void OnSubTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsPeopleSubTab));
        OnPropertyChanged(nameof(IsWorkplacesSubTab));
        OnPropertyChanged(nameof(IsDutySubTab));
        OnPropertyChanged(nameof(IsStatsSubTab));
        if (value == 3) RecomputeStatistics(); // picks up unsaved target edits
    }

    // ---- Statistics (phase 2) ----

    public ObservableCollection<PersonStatItem> Stats { get; } = [];

    [ObservableProperty]
    public partial PersonStatItem? SelectedStat { get; set; }

    public bool HasSelectedStat => SelectedStat is not null;

    partial void OnSelectedStatChanged(PersonStatItem? value) => OnPropertyChanged(nameof(HasSelectedStat));

    [ObservableProperty]
    public partial string? HistoryStatusText { get; set; }

    [ObservableProperty]
    public partial bool IsHistoryRunning { get; set; }

    public bool CanUpdateHistory => !IsHistoryRunning;

    partial void OnIsHistoryRunningChanged(bool value) => OnPropertyChanged(nameof(CanUpdateHistory));

    /// <summary>Wire on page appearing / disappearing: the history download keeps running either way.</summary>
    public void Attach() => _historyService.Changed += OnHistoryChanged;

    public void Detach() => _historyService.Changed -= OnHistoryChanged;

    private DateTime _lastStatsRecompute;

    private void OnHistoryChanged() => MainThread.BeginInvokeOnMainThread(() =>
    {
        UpdateHistoryStatus();
        if (!_historyService.IsRunning)
        {
            // Finished: the history may know people/workplaces/duty slots the lists do not have yet (edits are kept).
            if (IsIdle && IsAccessGranted)
            {
                CollectLocalData();
                Rebuild();
            }
            RecomputeStatistics();
        }
        // Every downloaded page raises this; recomputing the whole table every couple of seconds is wasted work.
        else if (DateTime.UtcNow - _lastStatsRecompute > TimeSpan.FromSeconds(15))
            RecomputeStatistics();
    });

    [RelayCommand]
    private void UpdateHistory()
    {
        if (IsAccessDenied) return;
        _ = _historyService.UpdateAsync();
        UpdateHistoryStatus();
    }

    private void UpdateHistoryStatus()
    {
        IsHistoryRunning = _historyService.IsRunning;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (have, total) = _historyService.Coverage(today);
        var window = $"Historie od {OpicentrumHistoryService.WindowStart(today).ToString("d. M. yyyy", Czech)}: {have}/{total} částí staženo.";
        HistoryStatusText = _historyService.ProgressText
                            ?? (_historyService.ErrorText is { } error ? $"{window} {error}" : window);
    }

    private void RecomputeStatistics()
    {
        _lastStatsRecompute = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var stats = OpicentrumStaffingStatistics.Compute(_historyService.Data, CurrentConfig(), OpicentrumHistoryService.WindowStart(today), today);

        var selectedId = SelectedStat?.PersonId;
        Stats.Clear();
        foreach (var stat in stats.Where(s => s.WorkedDays > 0 || s.DutyCount > 0)
                     .OrderByDescending(s => s.DeviationPoints).ThenBy(s => s.PersonName, StringComparer.Create(Czech, ignoreCase: true)))
            Stats.Add(new PersonStatItem(stat, stat.DeviationPoints >= UnfairDeviationPoints));
        SelectedStat = Stats.FirstOrDefault(s => s.PersonId == selectedId);

        // The doctors' tab shows the actual share next to each target.
        var byPerson = stats.ToDictionary(s => s.PersonId);
        foreach (var person in People)
        {
            var actual = byPerson.TryGetValue(person.PersonId, out var stat) ? stat.Shares.ToDictionary(s => s.WorkplaceId, s => s.ActualPercent) : [];
            foreach (var share in person.Shares)
                share.ActualText = actual.TryGetValue(share.WorkplaceId, out var pct) && pct >= 0.5 ? $"{pct:0} %" : string.Empty;
        }
    }

    private OpicentrumStaffingConfig CurrentConfig() => new(
        _config.UpdatedAtUtc,
        People.Select(p => p.ToProfile()).ToList(),
        Workplaces.Select(w => w.ToProfile()).ToList(),
        DutySlots.Select(d => d.ToMapping()).ToList());

    [RelayCommand]
    private void SelectSubTab(string index)
    {
        if (int.TryParse(index, out var i)) SubTab = i;
    }

    [ObservableProperty]
    public partial StaffProfileItem? SelectedPerson { get; set; }

    public bool HasSelectedPerson => SelectedPerson is not null;
    public bool HasNoSelectedPerson => SelectedPerson is null;

    partial void OnSelectedPersonChanged(StaffProfileItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedPerson));
        OnPropertyChanged(nameof(HasNoSelectedPerson));
    }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [ObservableProperty]
    public partial bool IsAccessDenied { get; set; }

    public bool IsAccessGranted => !IsAccessDenied;

    partial void OnIsAccessDeniedChanged(bool value) => OnPropertyChanged(nameof(IsAccessGranted));

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _currentUserService.InitializeAsync();
        IsAccessDenied = _rolePreviewService.Resolve(_currentUserService.Current.Role) is not Role.Admin;
        if (IsAccessDenied) SubTab = -1; // no sub-tab content over the "Admin only" message
        if (IsAccessDenied || IsBusy) return;

        IsBusy = true;
        ErrorText = null;
        try
        {
            try
            {
                _config = await Task.Run(() => _configService.GetCachedAsync()) ?? OpicentrumStaffingConfig.Empty;
            }
            catch (Exception ex)
            {
                AppLog.Warning(nameof(OpicentrumStaffingViewModel), "loading the staffing config failed", ex);
                ErrorText = "Nastavení z relaye se nepodařilo načíst — bez něj nelze ukládat. Zkuste to znovu později.";
                return;
            }

            CollectLocalData();
            Rebuild();
            RecomputeStatistics();
            StatusText = _config.UpdatedAtUtc == DateTimeOffset.MinValue
                ? "Zatím neuloženo."
                : $"Uloženo {_config.UpdatedAtUtc.ToLocalTime().ToString("d. M. yyyy H:mm", Czech)}.";
        }
        finally
        {
            IsBusy = false;
        }

        // Fills in / refreshes the six months of history in the background (gently — see OpicentrumHistoryService).
        UpdateHistory();
    }

    /// <summary>Scrapes the default week and the <see cref="ScrapedPastWeeks"/> before it, plus this month's Volno, for people/workplaces this device has not seen yet.</summary>
    [RelayCommand]
    private async Task FetchFromOpicentrumAsync()
    {
        if (IsBusy || IsAccessDenied) return;
        IsBusy = true;
        ErrorText = null;
        try
        {
            var week = WeekMath.DefaultViewWeek(DateOnly.FromDateTime(DateTime.Today));
            for (var i = 0; i <= ScrapedPastWeeks; i++)
            {
                StatusText = $"Načítám týden {i + 1}/{ScrapedPastWeeks + 1}…";
                AddRoster(await Task.Run(() => _opicentrumSyncService.FetchFullRosterBoardAsync(week.AddDays(-7 * i))));
            }
            StatusText = "Načítám volno…";
            AddVolno(await Task.Run(() => _opicentrumSyncService.FetchVolnoForMonthAsync(DateOnly.FromDateTime(DateTime.Today))));

            Rebuild();
            RecomputeStatistics();
            StatusText = $"Načteno: {People.Count} lékařů, {Workplaces.Count} pracovišť. Nezapomeňte uložit.";
        }
        catch (Exception ex)
        {
            AppLog.Warning(nameof(OpicentrumStaffingViewModel), "fetching people/workplaces from Opicentrum failed", ex);
            ErrorText = "Načtení z Opicentra se nezdařilo (přihlašovací údaje uložené v Nastavení → Opicentrum?).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || IsAccessDenied) return;

        var overfull = People.FirstOrDefault(p => p.TotalPercent > 100);
        if (overfull is not null)
        {
            ErrorText = $"{overfull.PersonName}: součet cílových procent je {overfull.TotalPercent} %, nesmí přesáhnout 100 %.";
            SelectedPerson = overfull;
            SubTab = 0;
            return;
        }

        IsBusy = true;
        ErrorText = null;
        try
        {
            var config = CurrentConfig() with { UpdatedAtUtc = DateTimeOffset.UtcNow };
            await Task.Run(() => _configService.PublishAsync(config));
            _config = config;
            StatusText = $"Uloženo {config.UpdatedAtUtc.ToLocalTime().ToString("d. M. yyyy H:mm", Czech)}.";
        }
        catch (Exception ex)
        {
            AppLog.Warning(nameof(OpicentrumStaffingViewModel), "saving the staffing config failed", ex);
            ErrorText = "Uložení se nezdařilo — zkontrolujte připojení k relayi a že máte roli Admin.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>People, workplaces and duty slots this device already has locally — no network.</summary>
    private void CollectLocalData()
    {
        var anchor = WeekMath.DefaultViewWeek(DateOnly.FromDateTime(DateTime.Today));
        for (var i = -8; i <= 2; i++)
        {
            if (OpicentrumRosterBoardLocalCache.Load(anchor.AddDays(7 * i)) is { } roster)
                AddRoster(roster);
        }

        if (OpicentrumVolnoBoardLocalCache.Load() is { } volno)
            AddVolno(volno);

        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var d = today.AddDays(-7); d <= today.AddDays(31); d = d.AddDays(1))
            foreach (var duty in DutyRosterStore.Get(d))
                if (!string.IsNullOrWhiteSpace(duty.Position))
                    _knownDutySlots.Add(duty.Position.Trim());

        // The six months of history (phase 2) know far more people, workplaces and duty slots.
        var history = _historyService.Data;
        foreach (var week in history.Weeks.Values) AddRoster(week);
        foreach (var month in history.VolnoMonths.Values) AddVolno(month);
        foreach (var entry in history.DutyMonths.Values.SelectMany(m => m.Entries))
            _knownDutySlots.Add(entry.SlotLabel.Trim());
    }

    private void AddRoster(OpicentrumRosterBoardSnapshot roster)
    {
        foreach (var workplace in roster.Days.SelectMany(d => d.Workplaces))
        {
            if (workplace.WorkplaceId > 0 && workplace.WorkplaceId != UnassignedWorkplaceId)
                _knownWorkplaces[workplace.WorkplaceId] = workplace.WorkplaceName.Trim();
            foreach (var person in workplace.People)
                if (person.PersonId > 0)
                    _knownPeople[person.PersonId] = person.PersonName.Trim();
        }
    }

    private void AddVolno(OpicentrumVolnoSnapshot volno)
    {
        foreach (var person in volno.People)
            if (person.PersonId > 0)
                _knownPeople.TryAdd(person.PersonId, person.PersonName.Trim());
    }

    /// <summary>Rebuilds every list from the saved config merged with the known people/workplaces/slots, keeping unsaved edits.</summary>
    private void Rebuild()
    {
        var comparer = StringComparer.Create(Czech, ignoreCase: true);

        // Unsaved edits on screen win over the config they were loaded from.
        var editedPeople = People.ToDictionary(p => p.PersonId, p => p.ToProfile());
        var editedWorkplaces = Workplaces.ToDictionary(w => w.WorkplaceId, w => w.ToProfile());
        var editedSlots = DutySlots.ToDictionary(d => d.SlotLabel, d => d.ToMapping(), StringComparer.OrdinalIgnoreCase);

        var workplaceProfiles = new Dictionary<int, WorkplaceProfile>();
        foreach (var w in _config.Workplaces) workplaceProfiles[w.WorkplaceId] = w;
        foreach (var (id, w) in editedWorkplaces) workplaceProfiles[id] = w;
        foreach (var (id, name) in _knownWorkplaces)
            workplaceProfiles[id] = workplaceProfiles.TryGetValue(id, out var existing)
                ? existing with { WorkplaceName = name }
                : new WorkplaceProfile(id, name, StaffGrade.L1, AllowsTrainee: true, RequiredSpecialization: null, Headcount: null, ExcludedFromSuggestions: false);

        var sortedWorkplaces = workplaceProfiles.Values.OrderBy(w => w.WorkplaceName, comparer).ToList();

        WorkplaceOptions.Clear();
        WorkplaceOptions.Add(WorkplaceOption.None);
        foreach (var w in sortedWorkplaces) WorkplaceOptions.Add(new WorkplaceOption(w.WorkplaceId, w.WorkplaceName));

        Workplaces.Clear();
        foreach (var w in sortedWorkplaces) Workplaces.Add(new WorkplaceProfileItem(w));

        var peopleProfiles = new Dictionary<int, StaffProfile>();
        foreach (var p in _config.People) peopleProfiles[p.PersonId] = p;
        foreach (var (id, p) in editedPeople) peopleProfiles[id] = p;
        foreach (var (id, name) in _knownPeople)
            peopleProfiles[id] = peopleProfiles.TryGetValue(id, out var existing)
                ? existing with { PersonName = name }
                : new StaffProfile(id, name, StaffGrade.Unset, null, [], [], ExcludedFromSuggestions: false);

        var selectedId = SelectedPerson?.PersonId;
        People.Clear();
        foreach (var p in peopleProfiles.Values.OrderBy(p => p.PersonName, comparer))
            People.Add(new StaffProfileItem(p, sortedWorkplaces, WorkplaceOptions));
        ApplyFilter();
        SelectedPerson = People.FirstOrDefault(p => p.PersonId == selectedId);

        var slots = new Dictionary<string, DutySlotMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _config.DutySlots) slots[s.SlotLabel] = s;
        foreach (var (label, s) in editedSlots) slots[label] = s;
        foreach (var label in _knownDutySlots) slots.TryAdd(label, new DutySlotMapping(label, null));

        DutySlots.Clear();
        foreach (var s in slots.Values.OrderBy(s => s.SlotLabel, comparer))
            DutySlots.Add(new DutySlotItem(s, WorkplaceOptions));
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        FilteredPeople.Clear();
        foreach (var p in People)
            if (filter.Length == 0 || p.PersonName.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                FilteredPeople.Add(p);
    }
}

public sealed record GradeOption(StaffGrade Grade, string Label)
{
    public static IReadOnlyList<GradeOption> All { get; } =
    [
        new(StaffGrade.Unset, "—"),
        new(StaffGrade.L1N, "L1N"),
        new(StaffGrade.L1, "L1"),
        new(StaffGrade.L2, "L2"),
        new(StaffGrade.L3, "L3"),
        new(StaffGrade.L4, "L4"),
    ];

    public static GradeOption For(StaffGrade grade) => All.FirstOrDefault(o => o.Grade == grade) ?? All[0];

    public override string ToString() => Label;
}

public sealed record WorkplaceOption(int? WorkplaceId, string Label)
{
    public static WorkplaceOption None { get; } = new(null, "—");

    public override string ToString() => Label;
}

/// <summary>One editable doctor row of <see cref="OpicentrumStaffingViewModel"/>.</summary>
public sealed partial class StaffProfileItem : ObservableObject
{
    public StaffProfileItem(StaffProfile profile, IReadOnlyList<WorkplaceProfile> workplaces, ObservableCollection<WorkplaceOption> workplaceOptions)
    {
        PersonId = profile.PersonId;
        PersonName = profile.PersonName;
        WorkplaceOptions = workplaceOptions;
        SelectedGrade = GradeOption.For(profile.Grade);
        HomeWorkplace = workplaceOptions.FirstOrDefault(o => o.WorkplaceId == profile.HomeWorkplaceId) ?? WorkplaceOption.None;
        SpecializationsText = string.Join(", ", profile.Specializations);
        ExcludedFromSuggestions = profile.ExcludedFromSuggestions;

        var shares = profile.TargetShares.ToDictionary(s => s.WorkplaceId, s => s.Percent);
        foreach (var w in workplaces)
        {
            var share = new StaffShareItem(w.WorkplaceId, w.WorkplaceName, shares.TryGetValue(w.WorkplaceId, out var pct) ? pct.ToString(CultureInfo.InvariantCulture) : string.Empty);
            share.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TotalPercent));
            share.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TotalPercentText));
            Shares.Add(share);
        }
    }

    public int PersonId { get; }
    public string PersonName { get; }
    public IReadOnlyList<GradeOption> GradeOptions => GradeOption.All;
    public ObservableCollection<WorkplaceOption> WorkplaceOptions { get; }
    public ObservableCollection<StaffShareItem> Shares { get; } = [];

    [ObservableProperty]
    public partial GradeOption SelectedGrade { get; set; }

    [ObservableProperty]
    public partial WorkplaceOption HomeWorkplace { get; set; }

    [ObservableProperty]
    public partial string SpecializationsText { get; set; }

    [ObservableProperty]
    public partial bool ExcludedFromSuggestions { get; set; }

    /// <summary>Shown in the list: grade, or a hint that it is still missing.</summary>
    public string Summary => SelectedGrade.Grade == StaffGrade.Unset ? "grade nenastaven" : SelectedGrade.Label + (ExcludedFromSuggestions ? " · vyřazen" : string.Empty);

    public bool IsGradeMissing => SelectedGrade.Grade == StaffGrade.Unset && !ExcludedFromSuggestions;

    public int TotalPercent => Shares.Sum(s => s.Percent ?? 0);

    public string TotalPercentText => TotalPercent == 0
        ? "Bez cílových procent — rozdělí se podle historie a férového podílu."
        : TotalPercent < 100 ? $"Součet {TotalPercent} % — zbytek se rozdělí podle historie." : $"Součet {TotalPercent} %";

    partial void OnSelectedGradeChanged(GradeOption value)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsGradeMissing));
        PrefillSectionShare();
    }

    partial void OnHomeWorkplaceChanged(WorkplaceOption value) => PrefillSectionShare();

    partial void OnExcludedFromSuggestionsChanged(bool value)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsGradeMissing));
    }

    /// <summary>L4 = usually a section lead, around 50 % on their own section (user, 2026-10-25) — prefilled, never overwritten.</summary>
    private void PrefillSectionShare()
    {
        if (SelectedGrade?.Grade != StaffGrade.L4 || HomeWorkplace?.WorkplaceId is not { } home) return;
        if (Shares.FirstOrDefault(s => s.WorkplaceId == home) is { PercentText.Length: 0 } share)
            share.PercentText = "50";
    }

    public StaffProfile ToProfile() => new(
        PersonId,
        PersonName,
        SelectedGrade.Grade,
        HomeWorkplace.WorkplaceId,
        SpecializationsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
        Shares.Where(s => s.Percent > 0).Select(s => new StaffWorkplaceShare(s.WorkplaceId, s.Percent!.Value)).ToList(),
        ExcludedFromSuggestions);
}

/// <summary>A doctor's target share of one workplace, edited as text ("" = none).</summary>
public sealed partial class StaffShareItem(int workplaceId, string workplaceName, string percentText) : ObservableObject
{
    public int WorkplaceId { get; } = workplaceId;
    public string WorkplaceName { get; } = workplaceName;

    [ObservableProperty]
    public partial string PercentText { get; set; } = percentText;

    public int? Percent => int.TryParse(PercentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? Math.Clamp(p, 0, 100) : null;

    /// <summary>The actual share over the history window ("34 %"), empty when the doctor was never there.</summary>
    [ObservableProperty]
    public partial string ActualText { get; set; } = string.Empty;
}

/// <summary>One row of the statistics tab.</summary>
public sealed class PersonStatItem(PersonStat stat, bool isUnfair)
{
    public int PersonId => stat.PersonId;
    public string PersonName => stat.PersonName;
    public string GradeLabel => GradeOption.For(stat.Grade).Label;
    public string WorkedDaysText => stat.WorkedDays.ToString("0.#", CultureInfo.InvariantCulture);
    public int DutyCount => stat.DutyCount;
    public int UnassignedDays => stat.UnassignedDays;

    /// <summary>The three most frequent workplaces, "ARIM 45 % · Sál 3 30 % · …".</summary>
    public string TopText => string.Join(" · ", stat.Shares.Where(s => s.ActualPercent >= 0.5).Take(3).Select(s => $"{s.WorkplaceName} {s.ActualPercent:0} %"));

    public string DeviationText => stat.WorkedDays == 0 ? "—" : $"{stat.DeviationPoints:0} b. {(stat.HasTargets ? "od cíle" : "od průměru grade")}";

    public bool IsUnfair => isUnfair;

    public IReadOnlyList<ShareStatRow> Rows { get; } = stat.Shares.Select(s => new ShareStatRow(s)).ToList();
}

/// <summary>One workplace in the statistics detail: actual vs. target.</summary>
public sealed class ShareStatRow(WorkplaceShareStat share)
{
    public string WorkplaceName => share.WorkplaceName;
    public string DaysText => share.Days.ToString("0.#", CultureInfo.InvariantCulture);
    public string ActualText => $"{share.ActualPercent:0} %";
    public string TargetText => share.TargetPercent is { } t ? $"{t} %" : "—";
    public string DiffText => share.TargetPercent is { } t ? $"{share.ActualPercent - t:+0;-0;0} b." : string.Empty;
}

/// <summary>One editable workplace row of <see cref="OpicentrumStaffingViewModel"/>.</summary>
public sealed partial class WorkplaceProfileItem : ObservableObject
{
    public WorkplaceProfileItem(WorkplaceProfile profile)
    {
        WorkplaceId = profile.WorkplaceId;
        WorkplaceName = profile.WorkplaceName;
        SelectedMinGrade = GradeOption.For(profile.MinGrade);
        AllowsTrainee = profile.AllowsTrainee;
        RequiredSpecialization = profile.RequiredSpecialization ?? string.Empty;
        HeadcountText = profile.Headcount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ExcludedFromSuggestions = profile.ExcludedFromSuggestions;
    }

    public int WorkplaceId { get; }
    public string WorkplaceName { get; }

    /// <summary>L1N is never a minimum on its own (a newcomer is never alone), so it is not offered.</summary>
    public IReadOnlyList<GradeOption> MinGradeOptions { get; } = GradeOption.All.Where(o => o.Grade is not (StaffGrade.Unset or StaffGrade.L1N)).ToList();

    [ObservableProperty]
    public partial GradeOption SelectedMinGrade { get; set; }

    [ObservableProperty]
    public partial bool AllowsTrainee { get; set; }

    [ObservableProperty]
    public partial string RequiredSpecialization { get; set; }

    [ObservableProperty]
    public partial string HeadcountText { get; set; }

    [ObservableProperty]
    public partial bool ExcludedFromSuggestions { get; set; }

    public WorkplaceProfile ToProfile() => new(
        WorkplaceId,
        WorkplaceName,
        SelectedMinGrade.Grade is StaffGrade.Unset or StaffGrade.L1N ? StaffGrade.L1 : SelectedMinGrade.Grade,
        AllowsTrainee,
        string.IsNullOrWhiteSpace(RequiredSpecialization) ? null : RequiredSpecialization.Trim(),
        int.TryParse(HeadcountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null,
        ExcludedFromSuggestions);
}

/// <summary>One duty slot → workplace row of <see cref="OpicentrumStaffingViewModel"/>.</summary>
public sealed partial class DutySlotItem : ObservableObject
{
    public DutySlotItem(DutySlotMapping mapping, ObservableCollection<WorkplaceOption> workplaceOptions)
    {
        SlotLabel = mapping.SlotLabel;
        WorkplaceOptions = workplaceOptions;
        SelectedWorkplace = workplaceOptions.FirstOrDefault(o => o.WorkplaceId == mapping.WorkplaceId) ?? WorkplaceOption.None;
    }

    public string SlotLabel { get; }
    public ObservableCollection<WorkplaceOption> WorkplaceOptions { get; }

    [ObservableProperty]
    public partial WorkplaceOption SelectedWorkplace { get; set; }

    public DutySlotMapping ToMapping() => new(SlotLabel, SelectedWorkplace?.WorkplaceId);
}
