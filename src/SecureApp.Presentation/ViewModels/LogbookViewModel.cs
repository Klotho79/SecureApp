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
/// RBAC, current shape after a 2026-09-10 correction ("každý uživatel má právo zadávat výkony...
/// ale přidávat typy výkonů do výběrového menu jen modifer a admin" — every role may WORK the
/// Logbook day-to-day, only Modifier/Admin may edit its two CATALOGS): any role — Viewer included —
/// may tick a checklist and record a procedure entry (<see cref="CanRecordProcedure"/>, picking an
/// existing type from <see cref="ProcedureTypeOptions"/>); Modifier/Admin alike may create new
/// checklist templates and see the statistics rollup (<see cref="CanManageChecklists"/>/
/// <see cref="CanViewStatistics"/>); only Admin may add a new procedure TYPE to the catalog itself
/// (<see cref="CanManageProcedureCatalog"/> — "položky zadá admin"). Creating either catalog item
/// lives on the separate <see cref="LogbookManagePage"/> (2026-09-10 split), not here — this
/// page is the daily-use surface only.
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

    // --- Statistics ---

    [ObservableProperty]
    public partial ObservableCollection<LogbookStatItem> Statistics { get; set; }

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

    private async Task RefreshStatisticsAsync()
    {
        var entries = await _procedureEntryRepository.GetAllAsync();
        var typeById = _procedureTypes.ToDictionary(t => t.Id);

        var stats = entries
            .Where(e => typeById.ContainsKey(e.ProcedureTypeId))
            .GroupBy(e => e.ProcedureTypeId)
            .Select(g =>
            {
                var type = typeById[g.Key];
                return new LogbookStatItem(
                    type.Name,
                    type.Category,
                    Seen: g.Count(e => e.Level == LogbookCompetenceLevel.Seen),
                    Supervised: g.Count(e => e.Level == LogbookCompetenceLevel.Supervised),
                    Independent: g.Count(e => e.Level == LogbookCompetenceLevel.Independent));
            })
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .ToList();

        Statistics = new ObservableCollection<LogbookStatItem>(stats);
        HasNoStatistics = Statistics.Count == 0;
    }

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
            var entry = new LogbookProcedureEntry(SelectedProcedureType.Id, SelectedLevel, DateTimeOffset.UtcNow, NoteText);
            await _procedureEntryRepository.AddAsync(entry);
            NoteText = string.Empty;
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

/// <summary>One row of the statistics view — the live-count equivalent of one row of the reference logbook's "Kompetence dle…" tables.</summary>
public sealed record LogbookStatItem(string Name, LogbookProcedureCategory Category, int Seen, int Supervised, int Independent)
{
    public int Total => Seen + Supervised + Independent;

    public string SummaryText => $"viděl {Seen} · pod dohledem {Supervised} · samostatně {Independent}";

    public string CategoryLabel => Category switch
    {
        LogbookProcedureCategory.Workplace => "Pracoviště",
        LogbookProcedureCategory.Procedure => "Výkon",
        LogbookProcedureCategory.Situation => "Situace",
        _ => Category.ToString()
    };
}
