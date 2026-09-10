using System.Collections.ObjectModel;
using System.Windows.Input;
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
///
/// Also lists what already exists in each catalog, with a delete action per row (2026-09-10, same
/// message: "taky přidej úpravy položek (odstranění)") — deletion goes through
/// <see cref="ILogbookCatalogSyncService.DeleteChecklistAsync"/>/<c>DeleteProcedureTypeAsync</c> in
/// addition to the local repository, so it actually sticks instead of being silently re-pulled back
/// in by <see cref="LogbookViewModel.SyncCatalogFromRelayAsync"/> the next time any device loads the
/// Logbook (a locally-missing id looks identical to "never synced yet" otherwise).
/// </summary>
public sealed partial class LogbookManageViewModel : ObservableObject
{
    private readonly ILogbookChecklistRepository _checklistRepository;
    private readonly ILogbookProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDiagnosticsReporter _diagnosticsReporter;
    private readonly ILogbookCatalogSyncService _catalogSyncService;

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

    /// <summary>See <see cref="LogbookProcedureType.Abbreviation"/>'s own remarks.</summary>
    [ObservableProperty]
    public partial string NewProcedureTypeAbbreviation { get; set; }

    /// <summary>Czech labels for a plain string <c>Picker</c> — simpler and more reliable in XAML than binding a Picker straight to enum values. Index maps 1:1 to <see cref="LogbookProcedureCategory"/>'s declaration order.</summary>
    public IReadOnlyList<string> CategoryOptions { get; } = ["Pracoviště", "Výkon", "Situace"];

    [ObservableProperty]
    public partial int SelectedCategoryIndex { get; set; }

    private LogbookProcedureCategory NewProcedureTypeCategory => (LogbookProcedureCategory)SelectedCategoryIndex;

    // --- Existing items, each with its own delete action ---

    [ObservableProperty]
    public partial ObservableCollection<LogbookManageChecklistItem> Checklists { get; set; }

    [ObservableProperty]
    public partial bool HasNoChecklists { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<LogbookManageProcedureTypeItem> ProcedureTypes { get; set; }

    [ObservableProperty]
    public partial bool HasNoProcedureTypes { get; set; }

    public LogbookManageViewModel(
        ILogbookChecklistRepository checklistRepository,
        ILogbookProcedureTypeRepository procedureTypeRepository,
        ICurrentUserService currentUserService,
        IDiagnosticsReporter diagnosticsReporter,
        ILogbookCatalogSyncService catalogSyncService)
    {
        _checklistRepository = checklistRepository ?? throw new ArgumentNullException(nameof(checklistRepository));
        _procedureTypeRepository = procedureTypeRepository ?? throw new ArgumentNullException(nameof(procedureTypeRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _catalogSyncService = catalogSyncService ?? throw new ArgumentNullException(nameof(catalogSyncService));

        NewChecklistName = string.Empty;
        NewChecklistItemsText = string.Empty;
        NewProcedureTypeName = string.Empty;
        NewProcedureTypeAbbreviation = string.Empty;
        Checklists = [];
        ProcedureTypes = [];
        HasNoChecklists = true;
        HasNoProcedureTypes = true;
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

        await RefreshListsAsync();
    }

    private async Task RefreshListsAsync()
    {
        var checklists = await _checklistRepository.GetAllAsync();
        Checklists = new ObservableCollection<LogbookManageChecklistItem>(
            checklists.Select(c => new LogbookManageChecklistItem(c.Id, c.Name, c.Items.Count, DeleteChecklistCommand)));
        HasNoChecklists = Checklists.Count == 0;

        var types = await _procedureTypeRepository.GetAllAsync();
        ProcedureTypes = new ObservableCollection<LogbookManageProcedureTypeItem>(
            types.Select(t => new LogbookManageProcedureTypeItem(t.Id, t.Name, t.Abbreviation, DescribeCategory(t.Category), DeleteProcedureTypeCommand)));
        HasNoProcedureTypes = ProcedureTypes.Count == 0;
    }

    private static string DescribeCategory(LogbookProcedureCategory category) => category switch
    {
        LogbookProcedureCategory.Workplace => "Pracoviště",
        LogbookProcedureCategory.Procedure => "Výkon",
        LogbookProcedureCategory.Situation => "Situace",
        _ => category.ToString()
    };

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

            // Shares it with every other device (2026-09-10, user's own ask) — a failure here is
            // surfaced, unlike LogbookViewModel.SyncCatalogFromRelayAsync's own silent read-side
            // best-effort: this is the moment someone's actively waiting to know whether it worked,
            // not a background refresh. The checklist still exists locally either way.
            var shared = await _catalogSyncService.PublishChecklistAsync(template);
            StatusSuccessMessage = shared
                ? $"Check-list „{template.Name}“ byl vytvořen a sdílen se všemi."
                : $"Check-list „{template.Name}“ byl vytvořen lokálně, ale nepodařilo se ho sdílet s ostatními (zkontrolujte připojení k relay) — zatím ho uvidíte jen vy.";
            NewChecklistName = string.Empty;
            NewChecklistItemsText = string.Empty;
            await RefreshListsAsync();
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
        if (string.IsNullOrWhiteSpace(NewProcedureTypeAbbreviation))
        {
            StatusErrorMessage = "Zadejte zkratku výkonu (např. „CŽK“) — vede statistiku.";
            return;
        }

        try
        {
            var type = new LogbookProcedureType(NewProcedureTypeName, NewProcedureTypeAbbreviation, NewProcedureTypeCategory);
            await _procedureTypeRepository.AddAsync(type);

            var shared = await _catalogSyncService.PublishProcedureTypeAsync(type);
            StatusSuccessMessage = shared
                ? $"Typ výkonu „{type.Name}“ byl přidán a sdílen se všemi."
                : $"Typ výkonu „{type.Name}“ byl přidán lokálně, ale nepodařilo se ho sdílet s ostatními (zkontrolujte připojení k relay) — zatím ho uvidíte jen vy.";
            NewProcedureTypeName = string.Empty;
            NewProcedureTypeAbbreviation = string.Empty;
            await RefreshListsAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se přidat typ výkonu: {ex.Message}";
        }
    }

    /// <summary>
    /// Deletes a checklist both locally and from the shared relay catalog (2026-09-10) — see this
    /// class's own remarks on why both. Confirmation dialog lives in <c>LogbookManagePage</c>'s
    /// code-behind, this codebase's established convention; this method runs once confirmed.
    /// </summary>
    [RelayCommand]
    private async Task DeleteChecklistAsync(Guid id)
    {
        if (!CanManageChecklists || id == Guid.Empty) return;
        StatusErrorMessage = null;
        StatusSuccessMessage = null;

        try
        {
            await _checklistRepository.DeleteAsync(id);
            var deletedRemotely = await _catalogSyncService.DeleteChecklistAsync(id);
            StatusSuccessMessage = deletedRemotely
                ? "Check-list byl smazán u vás i pro ostatní."
                : "Check-list byl smazán lokálně, ale u ostatních se zatím může znovu objevit (zkontrolujte připojení k relay a zkuste to znovu).";
            await RefreshListsAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se smazat check-list: {ex.Message}";
        }
    }

    /// <summary>
    /// Same as <see cref="DeleteChecklistAsync"/>, for a procedure type — note this ALSO deletes
    /// every <see cref="LogbookProcedureEntry"/> this device has already logged against it (schema's
    /// own <c>ON DELETE CASCADE</c>, same as everywhere else FK-linked data cascades in this app);
    /// the confirmation dialog says so explicitly rather than leaving it a surprise.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProcedureTypeAsync(Guid id)
    {
        if (!CanManageProcedureCatalog || id == Guid.Empty) return;
        StatusErrorMessage = null;
        StatusSuccessMessage = null;

        try
        {
            await _procedureTypeRepository.DeleteAsync(id);
            var deletedRemotely = await _catalogSyncService.DeleteProcedureTypeAsync(id);
            StatusSuccessMessage = deletedRemotely
                ? "Typ výkonu byl smazán u vás i pro ostatní."
                : "Typ výkonu byl smazán lokálně, ale u ostatních se zatím může znovu objevit (zkontrolujte připojení k relay a zkuste to znovu).";
            await RefreshListsAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se smazat typ výkonu: {ex.Message}";
        }
    }
}

/// <summary>One row in the "Check-listy" management list — carries the shared <see cref="DeleteCommand"/> instance (bound per-item as <c>CommandParameter="{Binding Id}"</c>) rather than an <c>x:Reference</c> back to the page, same pattern <c>PendingActivationItem</c> already established.</summary>
public sealed record LogbookManageChecklistItem(Guid Id, string Name, int ItemCount, ICommand DeleteCommand);

/// <summary>Same shape as <see cref="LogbookManageChecklistItem"/>, for the procedure-type catalog.</summary>
public sealed record LogbookManageProcedureTypeItem(Guid Id, string Name, string Abbreviation, string CategoryLabel, ICommand DeleteCommand);
