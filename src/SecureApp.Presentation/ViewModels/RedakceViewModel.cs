using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Redakce (2026-10-13, user's own ask: "vsechny modifikace upravy a vkladani zprav atd dej do
/// nastenky jako jednu polozku na spodnim menu. Polozky podle pravomoci zachovat.") — one tab
/// consolidating every edit/post action that used to be scattered across the app: the notice-board
/// composer (used to live in Nastavení→Uživatel→Nástěnka), the sál-status-options curation (used
/// to live inline on Nástěnka itself), and library category/subcategory management (used to live in
/// Nastavení→Systém→Knihovna, moved out per the user's own follow-up ask "V nastaveni sytem kategorie
/// knihovny patri taky do redakce" — only the "Kategorie knihovny" management card moved; the "Klíč
/// sdílené knihovny" status card is informational, not an edit action, and stays in Settings), plus a
/// link into the existing Logbook catalog management screen. Editing the device's OWN sál status
/// stays inline on Nástěnka (NotificationsViewModel.RoomStatus.cs) — the user's own explicit call on
/// a follow-up question; only the Admin/Modifier-only actions moved here. "Polozky podle pravomoci
/// zachovat" — every card below keeps the exact same role gate it already had wherever it used to
/// live; nothing about WHO may do what changed, only WHERE the controls live. Library category
/// management is split across this file and <see cref="RedakceViewModel"/>'s own
/// RedakceViewModel.LibraryCategories.cs partial, same split SettingsViewModel already used.
/// </summary>
public sealed partial class RedakceViewModel : ObservableObject
{
    private readonly ICommunityBoardService _communityBoardService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRoomStatusService _roomStatusService;
    private readonly ISharedLibraryService _sharedLibraryService;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly IDiagnosticsReporter _diagnosticsReporter;
    private readonly RolePreviewService _rolePreviewService;

    [ObservableProperty]
    public partial bool CanPostToBoard { get; set; }

    [ObservableProperty]
    public partial string BoardMessageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? BoardStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasBoardStatus { get; set; }

    [ObservableProperty]
    public partial bool CanManageRoomStatusOptions { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> RoomStatusOptions { get; set; }

    [ObservableProperty]
    public partial string NewStatusOptionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanManageLogbook { get; set; }

    [ObservableProperty]
    public partial bool CanManageLibrary { get; set; }

    /// <summary>True once loaded and none of the cards above apply — a plain Viewer with no editorial role sees this instead of an empty page.</summary>
    [ObservableProperty]
    public partial bool HasNoEditorialAccess { get; set; }

    partial void OnBoardStatusTextChanged(string? value) => HasBoardStatus = !string.IsNullOrEmpty(value);

    /// <summary>Raised for the Page (which alone can push a MAUI navigation) to open Logbook/Library management — same MAUI-free-ViewModel split this codebase already established (see NotificationsViewModel.RequestNavigate).</summary>
    public event Action<string>? RequestNavigate;

    public RedakceViewModel(
        ICommunityBoardService communityBoardService,
        ICurrentUserService currentUserService,
        IRoomStatusService roomStatusService,
        ISharedLibraryService sharedLibraryService,
        ITransportSettingsRepository transportSettingsRepository,
        IDiagnosticsReporter diagnosticsReporter,
        RolePreviewService rolePreviewService)
    {
        _communityBoardService = communityBoardService ?? throw new ArgumentNullException(nameof(communityBoardService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _roomStatusService = roomStatusService ?? throw new ArgumentNullException(nameof(roomStatusService));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _rolePreviewService = rolePreviewService ?? throw new ArgumentNullException(nameof(rolePreviewService));
        RoomStatusOptions = [];
        LibraryTopCategories = [];
        LibrarySubcategories = [];
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _currentUserService.InitializeAsync();
        var role = _rolePreviewService.Resolve(_currentUserService.Current.Role);

        // Same role gate POST /board already enforces server-side (see HttpCommunityBoardService's
        // own remarks) — mirrored here, same as it was in SettingsViewModel before this moved.
        CanPostToBoard = role is Role.Admin or Role.Modifier;

        // Same role gate POST /room-status/options already enforces server-side (see
        // IRoomStatusService's own remarks).
        CanManageRoomStatusOptions = role is Role.Admin or Role.Modifier;
        if (CanManageRoomStatusOptions)
        {
            try
            {
                RoomStatusOptions = new ObservableCollection<string>(await _roomStatusService.GetStatusOptionsAsync());
            }
            catch
            {
                RoomStatusOptions = [];
            }
        }

        // Exact same condition as LogbookViewModel.CanManageAnything, which gates its own "⚙ Správa"
        // button — kept in sync deliberately rather than referencing that ViewModel instance.
        CanManageLogbook =
            RoleAccessPolicy.IsAllowed(role, RbacAction.ManageLogbookChecklists) ||
            RoleAccessPolicy.IsAllowed(role, RbacAction.ManageLogbookProcedureCatalog);

        // Same gate the Library browse page's own add button used to use (see
        // RedakceViewModel.LibraryCategories.cs's own remarks on where this moved from).
        CanManageLibrary = role != Role.Viewer;
        if (CanManageLibrary)
        {
            await RefreshLibraryCategoriesAsync();
        }

        HasNoEditorialAccess = !CanPostToBoard && !CanManageRoomStatusOptions && !CanManageLogbook && !CanManageLibrary;
    }

    [RelayCommand]
    private async Task PostToBoardAsync()
    {
        var text = BoardMessageText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            BoardStatusText = "Zpráva je prázdná.";
            return;
        }

        try
        {
            await _communityBoardService.PostAsync(text);
            BoardMessageText = string.Empty;
            BoardStatusText = "Odesláno na nástěnku.";
        }
        catch (Exception ex)
        {
            BoardStatusText = $"Nepodařilo se odeslat: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddStatusOptionAsync()
    {
        var text = NewStatusOptionText.Trim();
        if (string.IsNullOrWhiteSpace(text) || RoomStatusOptions.Contains(text)) return;

        var updated = RoomStatusOptions.Append(text).ToList();
        if (await _roomStatusService.SetStatusOptionsAsync(updated))
        {
            NewStatusOptionText = string.Empty;
            RoomStatusOptions = new ObservableCollection<string>(updated);
        }
    }

    [RelayCommand]
    private async Task RemoveStatusOptionAsync(string? option)
    {
        if (option is null) return;
        var updated = RoomStatusOptions.Where(o => o != option).ToList();
        if (await _roomStatusService.SetStatusOptionsAsync(updated))
            RoomStatusOptions = new ObservableCollection<string>(updated);
    }

    [RelayCommand]
    private void OpenLogbookManage() => RequestNavigate?.Invoke(nameof(Views.LogbookManagePage));
}
