using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Admin/Modifier-only Opicentrum roster board (2026-10-21, the user's own ask: "potrebuju mit
/// nahled na kompletni obsateni pozic vcetne nezarazenych a zaroven nahled na provoz salu...
/// nahled a obsluhu volna"). Two sub-tabs — "Pozice" (position occupancy, with write-back move/
/// also-assign — see <c>OpicentrumBoardViewModel.Positions.cs</c>) and "Volno" (read-only leave
/// overview this round — see <c>OpicentrumBoardViewModel.Volno.cs</c>'s own remarks on why) — same
/// Button+DataTrigger sub-tab convention <c>SettingsViewModel</c> already uses, each sub-tab's
/// concerns split into its own partial file, same precedent as <c>SettingsViewModel.Opicentrum.cs</c>
/// (one class, not composed sub-ViewModels — this codebase doesn't inject a ViewModel into another).
///
/// Both tabs read a RELAY-CACHED snapshot, never scraping opicentrum.cz on open — the user's
/// explicit efficiency ask ("nevytezovat" the external site). A background job
/// (<c>App.RunBackgroundOpicentrumRosterBoardPublishAsync</c>/<c>...VolnoBoardPublishAsync</c>)
/// keeps that cache fresh; the "Obnovit" button on each tab is the only user action that triggers
/// a live scrape, for when an admin needs fresher data right now.
///
/// Access is gated here AND on the nav entry point that reaches this page
/// (<c>NotificationsViewModel.OpicentrumBoard.cs</c>'s <c>CanSeeOpicentrumBoard</c>) — belt-and-
/// suspenders, same style as <c>RoomStatusViewModel</c>. A Viewer (or role-preview-as-Viewer) who
/// somehow reaches this page directly sees <see cref="IsAccessDenied"/>'s message instead of data.
/// </summary>
public sealed partial class OpicentrumBoardViewModel : ObservableObject
{
    private readonly IOpicentrumSyncService _opicentrumSyncService;
    private readonly IOpicentrumRosterBoardService _rosterBoardCacheService;
    private readonly IOpicentrumVolnoBoardService _volnoBoardCacheService;
    private readonly ICurrentUserService _currentUserService;
    private readonly RolePreviewService _rolePreviewService;

    [ObservableProperty]
    public partial int BoardSubTab { get; set; }

    public bool IsPositionsSubTab => BoardSubTab == 0;
    public bool IsVolnoSubTab => BoardSubTab == 1;

    partial void OnBoardSubTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsPositionsSubTab));
        OnPropertyChanged(nameof(IsVolnoSubTab));
    }

    [RelayCommand]
    private void SelectSubTab(string index)
    {
        if (!int.TryParse(index, out var i)) return;
        BoardSubTab = i;
        if (i == 1 && !_volnoLoaded && !IsAccessDenied)
        {
            _volnoLoaded = true;
            _ = LoadVolnoAsync();
        }
    }

    // Volno (58 people x ~30 days = ~1700 cells) is only built once its tab is first opened: building
    // it on every page open froze the UI thread for seconds before the Pozice board could paint
    // (ui_stall metric from the user's S23+, 2026-10-22).
    private bool _volnoLoaded;

    [ObservableProperty]
    public partial bool IsAccessDenied { get; set; }

    /// <summary>Converter-free inverse of <see cref="IsAccessDenied"/>, same derived-bool pattern as the rest of this codebase's XAML bindings.</summary>
    public bool IsAccessGranted => !IsAccessDenied;

    partial void OnIsAccessDeniedChanged(bool value) => OnPropertyChanged(nameof(IsAccessGranted));

    /// <summary>The "Profily" button (position suggestions, POZICE_NAVRH_PLAN.md) — Admin on the PC only.</summary>
    [ObservableProperty]
    public partial bool CanEditStaffing { get; set; }

    public OpicentrumBoardViewModel(
        IOpicentrumSyncService opicentrumSyncService,
        IOpicentrumRosterBoardService rosterBoardCacheService,
        IOpicentrumVolnoBoardService volnoBoardCacheService,
        ICurrentUserService currentUserService,
        RolePreviewService rolePreviewService)
    {
        _opicentrumSyncService = opicentrumSyncService ?? throw new ArgumentNullException(nameof(opicentrumSyncService));
        _rosterBoardCacheService = rosterBoardCacheService ?? throw new ArgumentNullException(nameof(rosterBoardCacheService));
        _volnoBoardCacheService = volnoBoardCacheService ?? throw new ArgumentNullException(nameof(volnoBoardCacheService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _rolePreviewService = rolePreviewService ?? throw new ArgumentNullException(nameof(rolePreviewService));
        UpdatePositionsWeekLabel();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _currentUserService.InitializeAsync();
        var role = _rolePreviewService.Resolve(_currentUserService.Current.Role);
        IsAccessDenied = role is not (Role.Admin or Role.Modifier);
        CanEditStaffing = role == Role.Admin && DeviceInfo.Current.Platform == DevicePlatform.WinUI;
        if (IsAccessDenied) return;

        await LoadPositionsAsync();
        if (IsVolnoSubTab && !_volnoLoaded)
        {
            _volnoLoaded = true;
            await LoadVolnoAsync();
        }
    }
}
