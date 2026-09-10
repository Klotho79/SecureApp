using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// The Logbook tab (2026-09-09) — digitizes the reference "Příručka a logbook začínajícího
/// anesteziologa" (KNTB Zlín ARIM): a list of checklists (tap one to work through it — see
/// <see cref="LogbookChecklistViewModel"/>) and a procedure log (record performing/observing a
/// catalog item at a competency tier, which accumulates into the statistics list below it — the
/// live-count equivalent of the reference logbook's "Kompetence dle…" tables, whose paper form is a
/// single checkbox+signature per row instead).
///
/// RBAC, current shape after two 2026-09-10 corrections ("každý uživatel má právo zadávat
/// výkony... ale přidávat typy výkonů do výběrového menu jen modifer a admin" — every role may
/// WORK the Logbook day-to-day, only Modifier/Admin may edit its two CATALOGS): any role — Viewer
/// included — may tick a checklist and record a procedure entry (<see cref="CanRecordProcedure"/>,
/// picking an existing type from <see cref="ProcedureTypeOptions"/>); Modifier/Admin alike may
/// create new checklist templates, add a new procedure TYPE to the catalog, and see the statistics
/// rollup (<see cref="CanManageChecklists"/>/<see cref="CanManageProcedureCatalog"/>/
/// <see cref="CanViewStatistics"/> — no Admin-only carve-out left anywhere here, see
/// <c>RoleAccessPolicy</c>'s own remarks for the brief Admin-only period this walks back). Creating
/// either catalog item lives on the separate <see cref="LogbookManagePage"/> (2026-09-10 split),
/// not here — this page is the daily-use surface only.
/// </summary>
public sealed partial class LogbookViewModel : ObservableObject
{
    private readonly ILogbookChecklistRepository _checklistRepository;
    private readonly ILogbookProcedureTypeRepository _procedureTypeRepository;
    private readonly ILogbookProcedureEntryRepository _procedureEntryRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDiagnosticsReporter _diagnosticsReporter;
    private readonly ILogbookCatalogSyncService _catalogSyncService;

    private IReadOnlyList<LogbookProcedureType> _procedureTypes = [];

    [ObservableProperty]
    public partial ObservableCollection<LogbookChecklistListItem> Checklists { get; set; }

    [ObservableProperty]
    public partial bool HasNoChecklists { get; set; }

    [ObservableProperty]
    public partial bool CanManageChecklists { get; set; }

    [ObservableProperty]
    public partial bool CanManageProcedureCatalog { get; set; }

    /// <summary>Gates the "⚙ Správa" button (2026-09-10) that navigates to <see cref="Views.LogbookManagePage"/> — visible whenever there's anything at all to manage there, whichever of the two underlying actions it actually is.</summary>
    [ObservableProperty]
    public partial bool CanManageAnything { get; set; }

    [ObservableProperty]
    public partial bool CanRecordProcedure { get; set; }

    [ObservableProperty]
    public partial bool CanViewStatistics { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    // --- Logging a procedure ---

    [ObservableProperty]
    public partial ObservableCollection<LogbookProcedureType> ProcedureTypeOptions { get; set; }

    [ObservableProperty]
    public partial LogbookProcedureType? SelectedProcedureType { get; set; }

    /// <summary>Same plain-string-Picker reasoning as <see cref="LogbookManageViewModel.CategoryOptions"/>. Index maps 1:1 to <see cref="LogbookCompetenceLevel"/>'s declaration order.</summary>
    public IReadOnlyList<string> LevelOptions { get; } = ["Viděl", "Pod dohledem", "Samostatně"];

    [ObservableProperty]
    public partial int SelectedLevelIndex { get; set; }

    private LogbookCompetenceLevel SelectedLevel => (LogbookCompetenceLevel)SelectedLevelIndex;

    [ObservableProperty]
    public partial string NoteText { get; set; }

    /// <summary>See <see cref="LogbookProcedureEntry.Place"/>'s own remarks.</summary>
    [ObservableProperty]
    public partial string PlaceText { get; set; }

    // --- Statistics ---

    [ObservableProperty]
    public partial ObservableCollection<LogbookStatGroupItem> Statistics { get; set; }

    [ObservableProperty]
    public partial bool HasNoStatistics { get; set; }

    public LogbookViewModel(
        ILogbookChecklistRepository checklistRepository,
        ILogbookProcedureTypeRepository procedureTypeRepository,
        ILogbookProcedureEntryRepository procedureEntryRepository,
        ICurrentUserService currentUserService,
        IDiagnosticsReporter diagnosticsReporter,
        ILogbookCatalogSyncService catalogSyncService)
    {
        _checklistRepository = checklistRepository ?? throw new ArgumentNullException(nameof(checklistRepository));
        _procedureTypeRepository = procedureTypeRepository ?? throw new ArgumentNullException(nameof(procedureTypeRepository));
        _procedureEntryRepository = procedureEntryRepository ?? throw new ArgumentNullException(nameof(procedureEntryRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _catalogSyncService = catalogSyncService ?? throw new ArgumentNullException(nameof(catalogSyncService));

        Checklists = [];
        ProcedureTypeOptions = [];
        Statistics = [];
        NoteText = string.Empty;
        PlaceText = string.Empty;
        HasNoStatistics = true;
    }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Error, value!, nameof(LogbookViewModel));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            await _currentUserService.InitializeAsync();
            var role = _currentUserService.Current.Role;
            CanManageChecklists = RoleAccessPolicy.IsAllowed(role, RbacAction.ManageLogbookChecklists);
            CanManageProcedureCatalog = RoleAccessPolicy.IsAllowed(role, RbacAction.ManageLogbookProcedureCatalog);
            CanManageAnything = CanManageChecklists || CanManageProcedureCatalog;
            CanRecordProcedure = RoleAccessPolicy.IsAllowed(role, RbacAction.RecordLogbookProcedure);
            CanViewStatistics = RoleAccessPolicy.IsAllowed(role, RbacAction.ViewLogbookStatistics);

            // Pulls in whatever another device has published to the shared catalog since this
            // device last loaded (2026-09-10, user's own ask — see SyncCatalogFromRelayAsync's own
            // remarks) before reading the local repositories below, so a fresh item shows up in the
            // same page load that fetched it, not a load after.
            await SyncCatalogFromRelayAsync();

            var checklists = await _checklistRepository.GetAllAsync();
            Checklists = new ObservableCollection<LogbookChecklistListItem>(
                checklists.Select(c => new LogbookChecklistListItem(c.Id, c.Name, c.Items.Count)));
            HasNoChecklists = Checklists.Count == 0;

            _procedureTypes = await _procedureTypeRepository.GetAllAsync();
            ProcedureTypeOptions = new ObservableCollection<LogbookProcedureType>(_procedureTypes);
            SelectedProcedureType ??= ProcedureTypeOptions.FirstOrDefault();

            if (CanViewStatistics)
                await RefreshStatisticsAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se načíst logbook: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Pulls any checklist/procedure-type this device doesn't have yet from the relay's shared
    /// catalog (2026-09-10, user's own ask: "nové výkony nebo nové check listy se mají projevit u
    /// všech uživatelů") — best-effort and silent (unlike a publish failure, which
    /// <c>LogbookManageViewModel</c> DOES surface to the user, since that's the moment someone is
    /// actively waiting to know whether sharing worked): a relay that's unreachable right now just
    /// means this device keeps whatever it already has locally and tries again the next time this
    /// page loads, same policy every other read-side sync in this app already follows. Matches by
    /// id (see <see cref="LogbookChecklistTemplate"/>/<see cref="LogbookProcedureType"/>'s own
    /// <c>Guid id</c> constructors) so a repeat sync never creates a duplicate local copy.
    /// </summary>
    private async Task SyncCatalogFromRelayAsync()
    {
        try
        {
            var localChecklistIds = (await _checklistRepository.GetAllAsync()).Select(c => c.Id).ToHashSet();
            var remoteChecklists = await _catalogSyncService.FetchChecklistsAsync();
            foreach (var remote in remoteChecklists.Where(r => !localChecklistIds.Contains(r.Id)))
                await _checklistRepository.AddAsync(remote);

            var localTypeIds = (await _procedureTypeRepository.GetAllAsync()).Select(t => t.Id).ToHashSet();
            var remoteTypes = await _catalogSyncService.FetchProcedureTypesAsync();
            foreach (var remote in remoteTypes.Where(r => !localTypeIds.Contains(r.Id)))
                await _procedureTypeRepository.AddAsync(remote);
        }
        catch
        {
            // Best-effort — see this method's own remarks.
        }
    }

    /// <summary>
    /// Rebuilt (2026-09-10, "statistiku je třeba lépe formátovat... aby ve statistice bylo vidět
    /// zkratka výkonu, počet a po rozkliknutí ostatní údaje") — one collapsed row per procedure
    /// type leading with its <see cref="LogbookProcedureType.Abbreviation"/> and a total count
    /// (instead of the old always-expanded Seen/Supervised/Independent breakdown text), each
    /// carrying its own individual <see cref="LogbookProcedureEntry"/> rows (date, level, note,
    /// place) for <see cref="LogbookStatGroupItem.ToggleExpandedCommand"/> to reveal — see that
    /// class's own remarks for why it's a small <c>ObservableObject</c> rather than a plain record.
    /// </summary>
    private async Task RefreshStatisticsAsync()
    {
        var entries = await _procedureEntryRepository.GetAllAsync(); // already ordered by performed_at_utc DESC
        var typeById = _procedureTypes.ToDictionary(t => t.Id);

        var groups = entries
            .Where(e => typeById.ContainsKey(e.ProcedureTypeId))
            .GroupBy(e => e.ProcedureTypeId)
            .Select(g =>
            {
                var type = typeById[g.Key];
                var entryItems = g
                    .Select(e => new LogbookStatEntryItem(e.PerformedAtUtc.LocalDateTime.ToString("g"), DescribeLevel(e.Level), e.Note, e.Place))
                    .ToList();
                return new LogbookStatGroupItem(type.Abbreviation, type.Name, DescribeCategory(type.Category), type.Category, entryItems);
            })
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Abbreviation)
            .ToList();

        Statistics = new ObservableCollection<LogbookStatGroupItem>(groups);
        HasNoStatistics = Statistics.Count == 0;
    }

    private static string DescribeLevel(LogbookCompetenceLevel level) => level switch
    {
        LogbookCompetenceLevel.Seen => "Viděl",
        LogbookCompetenceLevel.Supervised => "Pod dohledem",
        LogbookCompetenceLevel.Independent => "Samostatně",
        _ => level.ToString()
    };

    private static string DescribeCategory(LogbookProcedureCategory category) => category switch
    {
        LogbookProcedureCategory.Workplace => "Pracoviště",
        LogbookProcedureCategory.Procedure => "Výkon",
        LogbookProcedureCategory.Situation => "Situace",
        _ => category.ToString()
    };

    [RelayCommand]
    private async Task OpenChecklistAsync(LogbookChecklistListItem? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync($"{nameof(LogbookChecklistPage)}?checklistId={item.Id}");
    }

    [RelayCommand]
    private async Task RecordProcedureAsync()
    {
        if (!CanRecordProcedure) return;
        StatusErrorMessage = null;
        if (SelectedProcedureType is null)
        {
            StatusErrorMessage = "Vyberte typ výkonu.";
            return;
        }

        try
        {
            var entry = new LogbookProcedureEntry(SelectedProcedureType.Id, SelectedLevel, DateTimeOffset.UtcNow, NoteText, PlaceText);
            await _procedureEntryRepository.AddAsync(entry);
            NoteText = string.Empty;
            PlaceText = string.Empty;
            if (CanViewStatistics)
                await RefreshStatisticsAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se zaznamenat výkon: {ex.Message}";
        }
    }

    /// <summary>Navigates to the catalog-management page (2026-09-10) — see <see cref="Views.LogbookManagePage"/>'s own remarks. Plain push, same as <see cref="OpenChecklistAsync"/> above; that page loads its own RBAC flags and data independently on <c>OnAppearing</c>, same pattern every other page in this app already uses.</summary>
    [RelayCommand]
    private async Task OpenManageAsync()
    {
        if (!CanManageAnything) return;
        await Shell.Current.GoToAsync(nameof(Views.LogbookManagePage));
    }
}

public sealed record LogbookChecklistListItem(Guid Id, string Name, int ItemCount);

/// <summary>One individual logged instance within an expanded <see cref="LogbookStatGroupItem"/> — the "ostatní údaje" (date, level, note, place) revealed on tap (2026-09-10).</summary>
public sealed record LogbookStatEntryItem(string DateText, string LevelLabel, string? Note, string? Place)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool HasPlace => !string.IsNullOrEmpty(Place);
}

/// <summary>
/// One row of the statistics view (2026-09-10 reformat) — collapsed by default, leading with the
/// procedure type's <see cref="LogbookProcedureType.Abbreviation"/> and a total count; tapping
/// reveals the individual <see cref="Entries"/> underneath (date/level/note/place each). A small
/// <c>ObservableObject</c> rather than a plain record specifically because <see cref="IsExpanded"/>
/// is genuinely mutable per-row UI state that needs to notify the CollectionView's DataTemplate
/// live — the shared-command-on-a-record pattern this codebase uses elsewhere (e.g.
/// <c>GroupMemberItem</c>) is for delegating an ACTION back to the owning ViewModel, not for state
/// that's entirely local to the row itself, which is what this is.
/// </summary>
public sealed partial class LogbookStatGroupItem : ObservableObject
{
    public string Abbreviation { get; }
    public string Name { get; }
    public string CategoryLabel { get; }
    internal LogbookProcedureCategory Category { get; }
    public IReadOnlyList<LogbookStatEntryItem> Entries { get; }
    public int Count => Entries.Count;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public LogbookStatGroupItem(string abbreviation, string name, string categoryLabel, LogbookProcedureCategory category, IReadOnlyList<LogbookStatEntryItem> entries)
    {
        Abbreviation = abbreviation;
        Name = name;
        CategoryLabel = categoryLabel;
        Category = category;
        Entries = entries;
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
