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
/// single checkbox+signature per row instead). The catalog itself — which checklists exist, which
/// procedure types exist — is Admin-only (<c>RbacAction.ManageLogbookCatalog</c>, the user's own
/// explicit "položky zadá admin"); the add-forms for both stay collapsed behind their own toggle by
/// default, same reasoning as <c>GroupChatViewModel.IsMembersExpanded</c> — a management form nobody
/// but an admin ever opens shouldn't cost every other viewer screen space.
/// </summary>
public sealed partial class LogbookViewModel : ObservableObject
{
    private readonly ILogbookChecklistRepository _checklistRepository;
    private readonly ILogbookProcedureTypeRepository _procedureTypeRepository;
    private readonly ILogbookProcedureEntryRepository _procedureEntryRepository;
    private readonly ICurrentUserService _currentUserService;

    private IReadOnlyList<LogbookProcedureType> _procedureTypes = [];

    [ObservableProperty]
    public partial ObservableCollection<LogbookChecklistListItem> Checklists { get; set; }

    [ObservableProperty]
    public partial bool HasNoChecklists { get; set; }

    [ObservableProperty]
    public partial bool CanManageCatalog { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    // --- New checklist (admin-only, collapsed by default) ---

    [ObservableProperty]
    public partial bool IsAddingChecklist { get; set; }

    [ObservableProperty]
    public partial string NewChecklistName { get; set; }

    /// <summary>One item per line — the simplest input shape for a variable-length list in a plain Editor, matching how the reference PDF's own checklists read as one line per item.</summary>
    [ObservableProperty]
    public partial string NewChecklistItemsText { get; set; }

    // --- Procedure catalog (admin-only, collapsed by default) ---

    [ObservableProperty]
    public partial bool IsAddingProcedureType { get; set; }

    [ObservableProperty]
    public partial string NewProcedureTypeName { get; set; }

    /// <summary>Czech labels for a plain string <c>Picker</c> — simpler and more reliable in XAML than binding a Picker straight to enum values, which needs a converter to render/select correctly. Index maps 1:1 to <see cref="LogbookProcedureCategory"/>'s declaration order.</summary>
    public IReadOnlyList<string> CategoryOptions { get; } = ["Pracoviště", "Výkon", "Situace"];

    [ObservableProperty]
    public partial int SelectedCategoryIndex { get; set; }

    private LogbookProcedureCategory NewProcedureTypeCategory => (LogbookProcedureCategory)SelectedCategoryIndex;

    // --- Logging a procedure ---

    [ObservableProperty]
    public partial ObservableCollection<LogbookProcedureType> ProcedureTypeOptions { get; set; }

    [ObservableProperty]
    public partial LogbookProcedureType? SelectedProcedureType { get; set; }

    /// <summary>Same plain-string-Picker reasoning as <see cref="CategoryOptions"/>. Index maps 1:1 to <see cref="LogbookCompetenceLevel"/>'s declaration order.</summary>
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
        ICurrentUserService currentUserService)
    {
        _checklistRepository = checklistRepository ?? throw new ArgumentNullException(nameof(checklistRepository));
        _procedureTypeRepository = procedureTypeRepository ?? throw new ArgumentNullException(nameof(procedureTypeRepository));
        _procedureEntryRepository = procedureEntryRepository ?? throw new ArgumentNullException(nameof(procedureEntryRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

        Checklists = [];
        ProcedureTypeOptions = [];
        Statistics = [];
        NewChecklistName = string.Empty;
        NewChecklistItemsText = string.Empty;
        NewProcedureTypeName = string.Empty;
        NoteText = string.Empty;
        HasNoStatistics = true;
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            await _currentUserService.InitializeAsync();
            CanManageCatalog = RoleAccessPolicy.IsAllowed(_currentUserService.Current.Role, RbacAction.ManageLogbookCatalog);

            var checklists = await _checklistRepository.GetAllAsync();
            Checklists = new ObservableCollection<LogbookChecklistListItem>(
                checklists.Select(c => new LogbookChecklistListItem(c.Id, c.Name, c.Items.Count)));
            HasNoChecklists = Checklists.Count == 0;

            _procedureTypes = await _procedureTypeRepository.GetAllAsync();
            ProcedureTypeOptions = new ObservableCollection<LogbookProcedureType>(_procedureTypes);
            SelectedProcedureType ??= ProcedureTypeOptions.FirstOrDefault();

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
            await RefreshStatisticsAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se zaznamenat výkon: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleAddingChecklist() => IsAddingChecklist = !IsAddingChecklist;

    [RelayCommand]
    private async Task ConfirmAddChecklistAsync()
    {
        if (!CanManageCatalog) return;
        StatusErrorMessage = null;

        if (string.IsNullOrWhiteSpace(NewChecklistName))
        {
            StatusErrorMessage = "Zadejte název check-listu.";
            return;
        }

        try
        {
            var items = NewChecklistItemsText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var template = new LogbookChecklistTemplate(NewChecklistName, items);
            await _checklistRepository.AddAsync(template);

            NewChecklistName = string.Empty;
            NewChecklistItemsText = string.Empty;
            IsAddingChecklist = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se vytvořit check-list: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleAddingProcedureType() => IsAddingProcedureType = !IsAddingProcedureType;

    [RelayCommand]
    private async Task ConfirmAddProcedureTypeAsync()
    {
        if (!CanManageCatalog) return;
        StatusErrorMessage = null;

        if (string.IsNullOrWhiteSpace(NewProcedureTypeName))
        {
            StatusErrorMessage = "Zadejte název výkonu.";
            return;
        }

        try
        {
            var type = new LogbookProcedureType(NewProcedureTypeName, NewProcedureTypeCategory);
            await _procedureTypeRepository.AddAsync(type);

            NewProcedureTypeName = string.Empty;
            IsAddingProcedureType = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se vytvořit typ výkonu: {ex.Message}";
        }
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
