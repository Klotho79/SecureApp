using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Logbook catalog management (2026-09-10) — split out of <see cref="LogbookViewModel"/>'s own page,
/// the user's own explicit ask: "všechny modifikace bych dal mimo... nové kauzy do logu samozřejmě
/// ne, ale přidávání nových typů výkonů, check listů atd nové okno" (move every modification
/// elsewhere — recording an actual procedure stays on the main page, but adding new procedure TYPES
/// or checklist TEMPLATES belongs in its own page). Both forms are the exact same fields/commands
/// <see cref="LogbookViewModel"/> used to host inline (collapsed by default there); no longer
/// collapsed here since this whole page IS the "management" surface now — nothing competing for the
/// same screen space with a daily-use action, unlike before.
/// </summary>
public sealed partial class LogbookManageViewModel : ObservableObject
{
    private readonly ILogbookChecklistRepository _checklistRepository;
    private readonly ILogbookProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDiagnosticsReporter _diagnosticsReporter;

    [ObservableProperty]
    public partial bool CanManageChecklists { get; set; }

    [ObservableProperty]
    public partial bool CanManageProcedureCatalog { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    [ObservableProperty]
    public partial string? StatusSuccessMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusSuccess { get; set; }

    // --- New checklist ---

    [ObservableProperty]
    public partial string NewChecklistName { get; set; }

    /// <summary>One item per line — same reasoning as the original inline form this replaces.</summary>
    [ObservableProperty]
    public partial string NewChecklistItemsText { get; set; }

    // --- New procedure type ---

    [ObservableProperty]
    public partial string NewProcedureTypeName { get; set; }

    /// <summary>Czech labels for a plain string <c>Picker</c> — simpler and more reliable in XAML than binding a Picker straight to enum values. Index maps 1:1 to <see cref="LogbookProcedureCategory"/>'s declaration order.</summary>
    public IReadOnlyList<string> CategoryOptions { get; } = ["Pracoviště", "Výkon", "Situace"];

    [ObservableProperty]
    public partial int SelectedCategoryIndex { get; set; }

    private LogbookProcedureCategory NewProcedureTypeCategory => (LogbookProcedureCategory)SelectedCategoryIndex;

    public LogbookManageViewModel(
        ILogbookChecklistRepository checklistRepository,
        ILogbookProcedureTypeRepository procedureTypeRepository,
        ICurrentUserService currentUserService,
        IDiagnosticsReporter diagnosticsReporter)
    {
        _checklistRepository = checklistRepository ?? throw new ArgumentNullException(nameof(checklistRepository));
        _procedureTypeRepository = procedureTypeRepository ?? throw new ArgumentNullException(nameof(procedureTypeRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));

        NewChecklistName = string.Empty;
        NewChecklistItemsText = string.Empty;
        NewProcedureTypeName = string.Empty;
    }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Error, value!, nameof(LogbookManageViewModel));
    }

    partial void OnStatusSuccessMessageChanged(string? value) => HasStatusSuccess = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _currentUserService.InitializeAsync();
        var role = _currentUserService.Current.Role;
        CanManageChecklists = RoleAccessPolicy.IsAllowed(role, RbacAction.ManageLogbookChecklists);
        CanManageProcedureCatalog = RoleAccessPolicy.IsAllowed(role, RbacAction.ManageLogbookProcedureCatalog);
    }

    [RelayCommand]
    private async Task ConfirmAddChecklistAsync()
    {
        if (!CanManageChecklists) return;
        StatusErrorMessage = null;
        StatusSuccessMessage = null;

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

            StatusSuccessMessage = $"Check-list „{template.Name}“ byl vytvořen.";
            NewChecklistName = string.Empty;
            NewChecklistItemsText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se vytvořit check-list: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ConfirmAddProcedureTypeAsync()
    {
        if (!CanManageProcedureCatalog) return;
        StatusErrorMessage = null;
        StatusSuccessMessage = null;

        if (string.IsNullOrWhiteSpace(NewProcedureTypeName))
        {
            StatusErrorMessage = "Zadejte název výkonu.";
            return;
        }

        try
        {
            var type = new LogbookProcedureType(NewProcedureTypeName, NewProcedureTypeCategory);
            await _procedureTypeRepository.AddAsync(type);

            StatusSuccessMessage = $"Typ výkonu „{type.Name}“ byl přidán.";
            NewProcedureTypeName = string.Empty;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se přidat typ výkonu: {ex.Message}";
        }
    }
}
