using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Lets the local user set their display name and RBAC role (Milestone 3), connect this device
/// to a chat relay (Milestone 5), and view/copy this device's contact card. Its only MAUI touch
/// is <c>MainThread.BeginInvokeOnMainThread</c> in <see cref="StartObservingConnection"/> — same
/// light-touch-in-the-main-partial precedent as <see cref="ChatViewModel"/>/<see cref="DocumentViewerViewModel"/>;
/// the one genuinely MAUI-only command (Clipboard) lives in <c>SettingsViewModel.Actions.cs</c>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IMessagingService _messagingService;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly IMessageTransport _messageTransport;
    private readonly ISharedLibraryService _sharedLibraryService;
    private readonly IRelayAdminService _relayAdminService;
    private readonly IContactDirectoryService _contactDirectoryService;
    private readonly ISharedContactService _sharedContactService;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IDiagnosticsReporter _diagnosticsReporter;
    private readonly IOpicentrumSyncService _opicentrumSyncService;
    private readonly IUpdateService _updateService;
    private readonly INativeAppInstaller? _nativeAppInstaller;
    private readonly INativeUpdateDownloader? _nativeUpdateDownloader;
    private readonly IIdentityBackupService _identityBackupService;
    private readonly ILibraryTranslationService _libraryTranslationService;

    private EventHandler<TransportConnectionState>? _connectionStateHandler;
    private IDispatcherTimer? _activationPollTimer;
    private Guid? _pendingActivationRequestId;

    /// <summary>
    /// 2026-09-23, user's own explicit rule: "nový člen nebude nikdy admin a jen admin zatím může
    /// nastavit pravomoci" — a non-Admin device must never be able to pick Admin for itself from this
    /// Picker; recomputed in LoadAsync once the device's own CURRENTLY STORED role is known (not
    /// SelectedRole, which is what's about to be saved — see SaveAsync's own matching guard for why
    /// both layers check the stored role, not the in-flight selection). An already-Admin device can
    /// still pick any role, including demoting itself.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<Role> AvailableRoles { get; set; } = Enum.GetValues<Role>();

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    /// <summary>
    /// 2026-10-08, user's own ask: reconcile this device's own identity against "Soukromé kontakty
    /// ARIM" on every profile Save. ProfileFirstName/ProfileLastName/ProfilePhone/ProfileEmail are
    /// NEW fields (User.FirstName/LastName/Phone/Email) — <see cref="DisplayName"/> above stays
    /// exactly what it always was (the optional chat nick; User.DisplayName itself now falls back to
    /// FirstName+LastName when the nick is empty, see User's own remarks, so this page's existing
    /// DisplayName field needs zero behavior change).
    /// </summary>
    [ObservableProperty]
    public partial string ProfileFirstName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProfileLastName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProfilePhone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProfileEmail { get; set; } = string.Empty;

    /// <summary>
    /// 2026-10-08 — shown once this build ships, for every device that only ever had the old
    /// DisplayName/Role pair: a nudge to fill in the new profile fields so ARIM reconciliation
    /// (<see cref="SaveAsync"/>) has something to match against. Recomputed in LoadAsync; clears
    /// itself the moment both Jméno and Příjmení are filled in (no separate "dismiss" state needed —
    /// filling the fields in and saving IS the dismissal).
    /// </summary>
    [ObservableProperty]
    public partial bool HasIncompleteProfile { get; set; }

    [ObservableProperty]
    public partial string? ArimReconciliationStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasArimReconciliationStatus { get; set; }

    partial void OnArimReconciliationStatusTextChanged(string? value) => HasArimReconciliationStatus = !string.IsNullOrEmpty(value);

    [ObservableProperty]
    public partial Role SelectedRole { get; set; }

    [ObservableProperty]
    public partial bool IsSaved { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsAdmin { get; set; }

    /// <summary>Gates the "Spravovat knihovnu" entry card (2026-10-02 — consolidated here per the user's own ask, "veškerá správa do nastavení": every management surface lives in Settings, not scattered across other tabs). Admin or Modifier, same RoleAccessPolicy.UploadLibraryFile reasoning LibraryViewModel.CanModifyContent already uses — a pure Viewer-role device that's also been flagged as a document reviewer is a narrow edge case not covered here yet, deferred along with the rest of this Settings layout pass.</summary>
    [ObservableProperty]
    public partial bool CanManageLibrary { get; set; }

    /// <summary>Logbook tab show/hide (2026-09-09) — the user's own explicit ask. Stored via <c>Preferences</c> (a per-device display setting, not User data — see LoadAsync/OnIsLogbookVisibleChanged) rather than a new Domain entity/table; applies live, see <c>AppShell.RebuildTabBar</c>'s own remarks.</summary>
    [ObservableProperty]
    public partial bool IsLogbookVisible { get; set; }

    /// <summary>Chaty/Soubory/Kontakty tab show/hide (2026-09-16, user's own ask: "chci mít možnost schovávat jednotlivé menu kromě settings") — same per-device Preferences mechanism as <see cref="IsLogbookVisible"/>, generalized to every tab except Nastavení itself.</summary>
    [ObservableProperty]
    public partial bool IsChatsTabVisible { get; set; }

    [ObservableProperty]
    public partial bool IsFilesTabVisible { get; set; }

    [ObservableProperty]
    public partial bool IsContactsTabVisible { get; set; }

    [ObservableProperty]
    public partial bool IsNotificationsTabVisible { get; set; }

    // --- Relay (Milestone 5) ---

    [ObservableProperty]
    public partial string RelayEndpointText { get; set; }

    // --- Activation (2026-09-06, auto-approve added 2026-09-19) — replaces the invite-code paste-in
    // below for a new device joining the community: instead of typing in a code an admin handed
    // over out-of-band, the new device sends name + email + this device's own key fingerprint and
    // the relay approves it on receipt (2026-09-19, the user's own call — see Program.cs's
    // /activation/request remarks: the install link is the only real gate now). See
    // IMessageTransport.RequestActivationAsync/PollActivationAsync.

    [ObservableProperty]
    public partial string ActivationEmailText { get; set; }

    [ObservableProperty]
    public partial string? ActivationStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasActivationStatus { get; set; }

    [ObservableProperty]
    public partial string ConnectionStatusText { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsNotConnected { get; set; }

    [ObservableProperty]
    public partial bool IsRegistered { get; set; }

    [ObservableProperty]
    public partial bool IsNotRegistered { get; set; }

    [ObservableProperty]
    public partial string? RelayErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasRelayError { get; set; }

    [ObservableProperty]
    public partial bool IsBusyWithRelay { get; set; }

    [ObservableProperty]
    public partial bool CanUseRelayControls { get; set; }

    // --- Shared library key (Milestone 5 follow-up; fully automatic since 2026-09-11, manual
    // Generate/Show/Import UI removed 2026-09-19 — see SharedLibraryKeySync's own remarks: a device
    // acquires the key entirely on its own, via relay escrow or paired-session pull, retried every
    // sweep. Both status properties below are passive display only, never a control to operate. ---

    [ObservableProperty]
    public partial bool HasSharedLibraryKey { get; set; }

    /// <summary>Mirrors <see cref="HasSharedLibraryKey"/> — kept as its own bound property (this codebase's established pattern, see HasNoAdminSecret) so XAML never needs to negate a binding.</summary>
    [ObservableProperty]
    public partial bool HasNoSharedLibraryKey { get; set; }

    // --- Relay admin (Admin role only) ---
    //
    // AdminSecretInputText (2026-09-07) is deliberately NEVER persisted anywhere — the user's own
    // explicit call after reviewing the original design, which stored it in OS-backed secure
    // storage (Windows DPAPI etc.): that protects against someone without this device's own login,
    // but not against anything already running under it. Re-typed fresh before every single admin
    // action (list/deregister/generate invite/redeploy) and cleared immediately after each one —
    // see IRelayAdminService's own remarks for the same reasoning on the service side.

    [ObservableProperty]
    public partial string AdminSecretInputText { get; set; }

    [ObservableProperty]
    public partial string? DeployStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasDeployStatus { get; set; }

    [ObservableProperty]
    public partial string? AdminErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasAdminError { get; set; }

    [ObservableProperty]
    public partial bool IsBusyWithAdmin { get; set; }

    [ObservableProperty]
    public partial bool CanUseAdminControls { get; set; }

    // --- Relay admin: device management (2.1, 2026-09-17) — see IRelayAdminService.GetRegisteredDevicesAsync's
    // own remarks. Same "type the admin secret fresh for every action" discipline as the activations
    // card above — TryTakeAdminSecret is shared, not duplicated.

    [ObservableProperty]
    public partial ObservableCollection<RegisteredDeviceItem> RegisteredDevices { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDevices { get; set; }

    [ObservableProperty]
    public partial bool HasNoRegisteredDevices { get; set; }

    [ObservableProperty]
    public partial bool HasRegisteredDevices { get; set; }

    // --- Shared diagnostics log (2026-09-10) — see IDiagnosticsReporter's own remarks. Deliberately
    // NOT gated behind IsAdmin/the admin secret: the whole point is any device can report to it and
    // any device can read it, without needing an admin password each time — same reasoning
    // /directory/members and /library/files already apply.

    [ObservableProperty]
    public partial ObservableCollection<DiagnosticLogItem> DiagnosticLogEntries { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDiagnosticLog { get; set; }

    [ObservableProperty]
    public partial bool HasNoDiagnosticLogEntries { get; set; }

    [ObservableProperty]
    public partial bool HasDiagnosticLogEntries { get; set; }

    [ObservableProperty]
    public partial string? DiagnosticLogErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasDiagnosticLogError { get; set; }

    public SettingsViewModel(
        ICurrentUserService currentUserService,
        IMessagingService messagingService,
        ITransportSettingsRepository transportSettingsRepository,
        IMessageTransport messageTransport,
        ISharedLibraryService sharedLibraryService,
        IRelayAdminService relayAdminService,
        IContactDirectoryService contactDirectoryService,
        ISharedContactService sharedContactService,
        IDiagnosticsReporter diagnosticsReporter,
        IChatSessionRepository chatSessionRepository,
        IOpicentrumSyncService opicentrumSyncService,
        IUpdateService updateService,
        ICommunityBoardService communityBoardService,
        IIdentityBackupService identityBackupService,
        ILibraryTranslationService libraryTranslationService,
        INativeAppInstaller? nativeAppInstaller = null,
        INativeUpdateDownloader? nativeUpdateDownloader = null)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
        _relayAdminService = relayAdminService ?? throw new ArgumentNullException(nameof(relayAdminService));
        _contactDirectoryService = contactDirectoryService ?? throw new ArgumentNullException(nameof(contactDirectoryService));
        _sharedContactService = sharedContactService ?? throw new ArgumentNullException(nameof(sharedContactService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _opicentrumSyncService = opicentrumSyncService ?? throw new ArgumentNullException(nameof(opicentrumSyncService));
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _communityBoardService = communityBoardService ?? throw new ArgumentNullException(nameof(communityBoardService));
        _identityBackupService = identityBackupService ?? throw new ArgumentNullException(nameof(identityBackupService));
        _libraryTranslationService = libraryTranslationService ?? throw new ArgumentNullException(nameof(libraryTranslationService));
        _nativeAppInstaller = nativeAppInstaller;
        _nativeUpdateDownloader = nativeUpdateDownloader;

        DiagnosticLogEntries = [];
        HasNoDiagnosticLogEntries = true;
        DisplayName = string.Empty;
        RelayEndpointText = string.Empty;
        ActivationEmailText = string.Empty;
        ConnectionStatusText = "Odpojeno";
        IsNotConnected = true;
        IsNotRegistered = true;
        CanUseRelayControls = true;
        AdminSecretInputText = string.Empty;
        CanUseAdminControls = true;
        HasNoSharedLibraryKey = true;
        RegisteredDevices = [];
        HasNoRegisteredDevices = true;
        WorkplaceColorItems = [];
        InitializeUpdatesSection();
        InitializeLocalAiTranslationSection();
    }

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);

    partial void OnDisplayNameChanged(string value) => IsSaved = false;

    partial void OnSelectedRoleChanged(Role value)
    {
        IsSaved = false;
        IsAdmin = value == Role.Admin;
        CanManageLibrary = value != Role.Viewer;
    }

    partial void OnRelayErrorMessageChanged(string? value) => HasRelayError = !string.IsNullOrEmpty(value);

    partial void OnActivationStatusTextChanged(string? value) => HasActivationStatus = !string.IsNullOrEmpty(value);

    partial void OnIsBusyWithRelayChanged(bool value) => CanUseRelayControls = !value;

    partial void OnIsConnectedChanged(bool value) => IsNotConnected = !value;

    partial void OnIsRegisteredChanged(bool value) => IsNotRegistered = !value;

    partial void OnHasSharedLibraryKeyChanged(bool value) => HasNoSharedLibraryKey = !value;

    partial void OnAdminErrorMessageChanged(string? value) => HasAdminError = !string.IsNullOrEmpty(value);

    partial void OnIsBusyWithAdminChanged(bool value) => CanUseAdminControls = !value;

    partial void OnDeployStatusTextChanged(string? value) => HasDeployStatus = !string.IsNullOrEmpty(value);

    partial void OnHasNoRegisteredDevicesChanged(bool value) => HasRegisteredDevices = !value;

    partial void OnDiagnosticLogErrorMessageChanged(string? value) => HasDiagnosticLogError = !string.IsNullOrEmpty(value);

    partial void OnHasNoDiagnosticLogEntriesChanged(bool value) => HasDiagnosticLogEntries = !value;

    /// <summary>
    /// Writes through immediately, not gated behind the "Uložit" button — this is a per-device
    /// display preference (see <see cref="IsLogbookVisible"/>'s own remarks), not User data, so
    /// there's nothing to "save" beyond flipping the switch. <c>LoadAsync</c> below sets the
    /// initial value, which re-invokes this too — a harmless idempotent re-write of the same value.
    /// Also applies the change to the actual TabBar right away (2026-09-10, user's own ask: no
    /// restart needed) via <see cref="AppShell.ApplyTabVisibility"/> — <c>Shell.Current</c>
    /// is always the app's one <see cref="AppShell"/> instance in this app (there's only ever one
    /// Shell), so the cast is safe without a null-forgiving check beyond the `as` itself.
    /// </summary>
    partial void OnIsLogbookVisibleChanged(bool value) =>
        (Shell.Current as AppShell)?.ApplyTabVisibility(AppShell.LogbookVisibilityPreferenceKey, value);

    /// <summary>Same mechanism as <see cref="OnIsLogbookVisibleChanged"/>, one per hideable tab (2026-09-16).</summary>
    partial void OnIsChatsTabVisibleChanged(bool value) =>
        (Shell.Current as AppShell)?.ApplyTabVisibility(AppShell.ChatsTabVisibilityPreferenceKey, value);

    partial void OnIsFilesTabVisibleChanged(bool value) =>
        (Shell.Current as AppShell)?.ApplyTabVisibility(AppShell.FilesTabVisibilityPreferenceKey, value);

    partial void OnIsContactsTabVisibleChanged(bool value) =>
        (Shell.Current as AppShell)?.ApplyTabVisibility(AppShell.ContactsTabVisibilityPreferenceKey, value);

    partial void OnIsNotificationsTabVisibleChanged(bool value) =>
        (Shell.Current as AppShell)?.ApplyTabVisibility(AppShell.NotificationsTabVisibilityPreferenceKey, value);

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _currentUserService.InitializeAsync();
        DisplayName = _currentUserService.Current.Nick;
        ProfileFirstName = _currentUserService.Current.FirstName ?? string.Empty;
        ProfileLastName = _currentUserService.Current.LastName ?? string.Empty;
        ProfilePhone = _currentUserService.Current.Phone ?? string.Empty;
        ProfileEmail = _currentUserService.Current.Email ?? string.Empty;
        HasIncompleteProfile = string.IsNullOrWhiteSpace(ProfileFirstName) || string.IsNullOrWhiteSpace(ProfileLastName);
        SelectedRole = _currentUserService.Current.Role;
        // Both set explicitly here, not left to OnSelectedRoleChanged alone: Role.Admin is the
        // enum's default (0), so on an Admin device the assignment above is a same-value no-op —
        // CommunityToolkit's generated setter skips the change notification entirely when nothing
        // actually changed, so the partial method (and anything it sets) never runs on first load.
        IsAdmin = SelectedRole == Role.Admin;
        CanManageLibrary = SelectedRole != Role.Viewer;
        AvailableRoles = IsAdmin ? Enum.GetValues<Role>() : [Role.Modifier, Role.Viewer];
        RefreshCanPostToBoard();
        LoadThemeMode();
        LoadFontScale();
        LoadAccent();
        LoadChatAppearance();
        RefreshBackgroundRunStatus();
        IsSaved = false;
        ErrorMessage = null;
        IsLogbookVisible = Preferences.Default.Get(AppShell.LogbookVisibilityPreferenceKey, false);
        IsChatsTabVisible = Preferences.Default.Get(AppShell.ChatsTabVisibilityPreferenceKey, true);
        IsFilesTabVisible = Preferences.Default.Get(AppShell.FilesTabVisibilityPreferenceKey, true);
        IsContactsTabVisible = Preferences.Default.Get(AppShell.ContactsTabVisibilityPreferenceKey, true);
        IsNotificationsTabVisible = Preferences.Default.Get(AppShell.NotificationsTabVisibilityPreferenceKey, true);

        var configuration = await _transportSettingsRepository.GetAsync();
        // No saved endpoint yet (first time this device opens Settings) -> pre-fill the
        // community's one relay address instead of leaving the field blank for the user to guess.
        RelayEndpointText = configuration?.EndpointUri?.ToString() ?? RelayDefaults.DefaultEndpoint;
        UpdateDownloadShareUrl(RelayEndpointText);
        IsRegistered = configuration?.AssignedDeviceId is not null;
        IsConnected = _messageTransport.IsConnected;
        ConnectionStatusText = IsConnected ? "Připojeno" : "Odpojeno";

        await LoadOpicentrumStateAsync();
        LoadWorkplaceColorsState();

        // Resume polling an activation request that was still pending the last time this device
        // closed — otherwise a relaunch mid-activation would silently strand the request: nothing
        // would ever check on it again even though the relay auto-approves it almost immediately.
        if (!IsRegistered && configuration?.PendingActivationRequestId is { } pendingRequestId
            && Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var resumedEndpoint))
        {
            _pendingActivationRequestId = pendingRequestId;
            ActivationStatusText = "Aktivace probíhá…";
            StartActivationPolling(resumedEndpoint);
        }

        HasSharedLibraryKey = await _sharedLibraryService.HasSharedKeyAsync();
        await RefreshLibraryCategoriesAsync();

        // Diagnostic log auto-loads here since it needs no admin secret — LoadDiagnosticLogAsync
        // already catches its own failures into DiagnosticLogErrorMessage rather than throwing, so
        // a relay that's unreachable right now doesn't block the rest of this page from loading;
        // "⟳ Obnovit" retries it explicitly.
        if (IsAdmin)
            await LoadDiagnosticLogAsync();
    }

    /// <summary>True while <see cref="AdminSecretInputText"/> is validated and about to be used — factored out so every admin command below applies the identical check/clear pattern instead of repeating it.</summary>
    private bool TryTakeAdminSecret(out string adminSecret)
    {
        adminSecret = AdminSecretInputText;
        if (string.IsNullOrWhiteSpace(adminSecret))
        {
            AdminErrorMessage = "Nejprve zadejte admin heslo relay serveru (SECUREAPP_RELAY_ADMIN_SECRET).";
            return false;
        }

        // Cleared immediately, whether or not the call below actually succeeds — see
        // AdminSecretInputText's own remarks: it must never linger longer than one call.
        AdminSecretInputText = string.Empty;
        return true;
    }

    /// <summary>Fetches every registered device's staleness + pending-outbox depth (2.1, 2026-09-17) — same on-demand, admin-secret-per-call pattern as <see cref="GenerateInviteAsync"/>.</summary>
    [RelayCommand]
    private async Task LoadRegisteredDevicesAsync()
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }
        if (!TryTakeAdminSecret(out var adminSecret)) return;
        _devicesAdminSecret = adminSecret;
        await RefreshRegisteredDevicesAsync(endpoint, adminSecret);
    }

    // Held only while Settings stays open, so viewing several devices' logs doesn't need the secret
    // retyped each time — same session-scoped pattern as _managementAdminSecret; cleared in ClearMemberManagement.
    private string? _devicesAdminSecret;

    [ObservableProperty]
    public partial string? DeviceLogTitle { get; set; }

    [ObservableProperty]
    public partial string? DeviceLogText { get; set; }

    [ObservableProperty]
    public partial bool HasDeviceLog { get; set; }

    [RelayCommand]
    private Task ShowDeviceErrorsAsync(Guid deviceId) => ShowDeviceLogAsync(deviceId, "errors", "chyby");

    [RelayCommand]
    private Task ShowDeviceEventsAsync(Guid deviceId) => ShowDeviceLogAsync(deviceId, "metrics", "události");

    /// <summary>Loads one device's uploaded app log (newest first) — see Diagnostics.AppLogUploader for how it gets there.</summary>
    private async Task ShowDeviceLogAsync(Guid deviceId, string kind, string kindLabel)
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint)) return;
        if (string.IsNullOrEmpty(_devicesAdminSecret))
        {
            AdminErrorMessage = "Nejprve načtěte seznam zařízení (admin heslo + ⟳ Obnovit).";
            return;
        }

        var name = RegisteredDevices.FirstOrDefault(d => d.Id == deviceId)?.DisplayName ?? deviceId.ToString();
        try
        {
            var lines = await _relayAdminService.GetDeviceAppLogAsync(endpoint, _devicesAdminSecret, deviceId, kind);
            DeviceLogTitle = $"Log: {name} — {kindLabel} ({lines.Count} řádků, nejnovější nahoře)";
            DeviceLogText = lines.Count == 0
                ? "Zatím nic — zařízení log ještě neodeslalo (odesílá ho samo zhruba každé 3 minuty, když je připojené)."
                : string.Join("\n", lines.Reverse().Select(l => l.Replace('\t', ' ')));
            HasDeviceLog = true;
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Log se nepodařilo načíst: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseDeviceLog()
    {
        HasDeviceLog = false;
        DeviceLogText = null;
    }

    private async Task RefreshRegisteredDevicesAsync(Uri endpoint, string adminSecret)
    {
        IsLoadingDevices = true;
        try
        {
            var devices = await _relayAdminService.GetRegisteredDevicesAsync(endpoint, adminSecret);
            RegisteredDevices = new ObservableCollection<RegisteredDeviceItem>(GroupDevicesByPerson(devices));
            HasNoRegisteredDevices = RegisteredDevices.Count == 0;
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se načíst seznam zařízení: {ex.Message}";
        }
        finally
        {
            IsLoadingDevices = false;
        }
    }

    /// <summary>Platforms a person is expected to have SecureApp on — the order the coverage line lists them in. Keys are <c>DeviceInfo.Current.Platform.ToString()</c> values, as published to the relay directory.</summary>
    private static readonly (string Key, string Label)[] _coveredPlatforms =
        [("Android", "Android"), ("WinUI", "PC"), ("iOS", "iPhone"), ("MacCatalyst", "Mac")];

    /// <summary>
    /// Orders the admin device list by person (2026-10-09) — devices that reconciled against the same
    /// "Soukromé kontakty ARIM" row (<see cref="RegisteredDevice.ArimContactId"/>) sit together, and the
    /// first row of each person carries a header with which platforms that person has and which are
    /// missing ("is this person missing from PC/iPhone/Mac"). Devices never reconciled go last under
    /// their own header. A flat list with per-row headers instead of a grouped CollectionView —
    /// grouped CollectionViews have been unreliable on Android and this card needs nothing they add.
    /// </summary>
    private List<RegisteredDeviceItem> GroupDevicesByPerson(IReadOnlyList<RegisteredDevice> devices)
    {
        var result = new List<RegisteredDeviceItem>(devices.Count);

        var people = devices
            .Where(d => d.ArimContactId is not null)
            .GroupBy(d => d.ArimContactId!.Value)
            .OrderBy(g => g.Select(d => d.ArimContactName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "", StringComparer.CurrentCultureIgnoreCase);
        foreach (var person in people)
        {
            var name = person.Select(d => d.ArimContactName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Neznámý kontakt";
            var present = person.Select(d => d.Platform).Where(p => p is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var coverage = string.Join("  ·  ", _coveredPlatforms.Select(p => (present.Contains(p.Key) ? "✓ " : "✗ ") + p.Label));
            AddGroup($"👤 {name}", coverage, person);
        }

        var unlinked = devices.Where(d => d.ArimContactId is null).ToList();
        if (unlinked.Count > 0)
            AddGroup("❔ Nepřiřazená zařízení", "Zatím neuložila profil proti kontaktům ARIM.", unlinked);

        return result;

        void AddGroup(string header, string coverage, IEnumerable<RegisteredDevice> groupDevices)
        {
            var first = true;
            foreach (var device in groupDevices.OrderBy(d => d.Platform).ThenBy(d => d.DirectoryDisplayName ?? d.DisplayName))
            {
                var item = ToRegisteredDeviceItem(device);
                result.Add(first ? item with { GroupHeader = header, GroupCoverageText = coverage } : item);
                first = false;
            }
        }
    }

    private static string DescribePlatform(string? platform) => platform switch
    {
        null or "" => "neznámá platforma",
        _ => _coveredPlatforms.FirstOrDefault(p => string.Equals(p.Key, platform, StringComparison.OrdinalIgnoreCase)).Label ?? platform
    };

    private RegisteredDeviceItem ToRegisteredDeviceItem(RegisteredDevice device)
    {
        // DirectoryDisplayName/LastActiveAtUtc are null exactly when this device is currently hidden
        // from every OTHER device's member picker/directory too (see RegisteredDevice's own remarks)
        // — that staleness, not CreatedAtUtc, is what actually marks a ghost identity worth deregistering.
        var statusText = device.LastActiveAtUtc is { } lastActive
            ? $"Aktivní v adresáři — naposledy {lastActive.LocalDateTime:g}"
            : "Nikdy nepublikoval do adresáře, nebo je dávno neaktivní";
        var pendingText = device.PendingOutboxCount > 0
            ? $"⚠ {device.PendingOutboxCount} zpráv čeká na doručení tomuto zařízení"
            : "Žádné čekající zprávy";

        return new RegisteredDeviceItem(
            device.Id,
            $"{device.DirectoryDisplayName ?? device.DisplayName}  ({DescribePlatform(device.Platform)})",
            statusText,
            pendingText,
            device.PendingOutboxCount > 0,
            device.LastActiveAtUtc is null,
            DeregisterDeviceCommand,
            ShowDeviceErrorsCommand,
            ShowDeviceEventsCommand);
    }

    /// <summary>
    /// Deletes the device's credential, directory entry, and purges its stuck outbox queue (2.1,
    /// 2026-09-17) — see <c>RelayDatabase.DeregisterDevice</c>'s own remarks. Destructive and
    /// irreversible (the device would need to re-register/re-activate from scratch), so the
    /// confirmation dialog lives here, directly in this RelayCommand — same precedent
    /// <c>GroupChatViewModel.DeleteMessageAsync</c> already established for an in-VM confirm rather
    /// than routing through the page's code-behind.
    /// </summary>
    [RelayCommand]
    private async Task DeregisterDeviceAsync(Guid deviceId)
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }

        var target = RegisteredDevices.FirstOrDefault(d => d.Id == deviceId);
        var confirmed = await Shell.Current.DisplayAlert(
            "Odregistrovat zařízení",
            $"Opravdu odregistrovat '{target?.DisplayName ?? deviceId.ToString()}'? Zařízení se bude muset znovu aktivovat od začátku a jeho čekající zprávy budou zahozeny. Tuto akci nelze vrátit zpět.",
            "Odregistrovat", "Zrušit");
        if (!confirmed) return;

        if (!TryTakeAdminSecret(out var adminSecret)) return;

        try
        {
            await _relayAdminService.DeregisterDeviceAsync(endpoint, adminSecret, deviceId);
            AppLog.Event("admin.device.deregistered", ("device", deviceId));
            await RefreshRegisteredDevicesAsync(endpoint, adminSecret);
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se odregistrovat zařízení: {ex.Message}";
            AppLog.Error("Settings.DeregisterDevice", "deregister failed", ex);
        }
    }

    /// <summary>
    /// Asks the relay to redeploy from whatever code the last `git push` to the Pi already checked
    /// out — this call carries no code itself, just the request (see relay/ops/README.md and
    /// IRelayAdminService.RequestDeployAsync's own remarks for the full pipeline and why). Purely
    /// a convenience over SSHing into the Pi and running `docker compose build && up -d` by hand.
    /// </summary>
    [RelayCommand]
    private async Task RedeployRelayAsync()
    {
        AdminErrorMessage = null;
        DeployStatusText = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }

        if (!TryTakeAdminSecret(out var adminSecret)) return;

        IsBusyWithAdmin = true;
        try
        {
            await _relayAdminService.RequestDeployAsync(endpoint, adminSecret);
            DeployStatusText = "Nasazení vyžádáno — relay server se za pár sekund znovu sestaví a restartuje.";
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se vyžádat nasazení: {ex.Message}";
        }
        finally
        {
            IsBusyWithAdmin = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        // Defense in depth alongside AvailableRoles' own filtering above (2026-09-23, user's own
        // rule) — checks the CURRENTLY STORED role, not SelectedRole itself, since the whole point is
        // rejecting an attempt to move INTO Admin from something else, not blocking an already-Admin
        // device from keeping/changing its own role.
        if (SelectedRole == Role.Admin && _currentUserService.Current.Role != Role.Admin)
        {
            ErrorMessage = "Roli Admin může nastavit jen existující administrátor.";
            SelectedRole = _currentUserService.Current.Role;
            return;
        }

        try
        {
            await _currentUserService.SetCurrentUserAsync(DisplayName, SelectedRole);
            await _currentUserService.UpdateProfileAsync(ProfileFirstName, ProfileLastName, ProfilePhone, ProfileEmail);
            HasIncompleteProfile = string.IsNullOrWhiteSpace(ProfileFirstName) || string.IsNullOrWhiteSpace(ProfileLastName);
            IsSaved = true;

            // 2026-09-09: a real, repeatedly-reported bug — PublishSelfAsync (what actually pushes
            // this device's name into the relay's directory, which DirectoryNameResolver's whole
            // fix depends on) previously only ran from WebSocketMessageTransport.ConnectAsync, i.e.
            // only on a fresh CONNECT. Renaming yourself while ALREADY connected — the ordinary
            // case, nobody reconnects just to change their name — saved the new name locally but
            // never told the relay, so every peer kept seeing the OLD name indefinitely, sometimes
            // for hours, until this device's connection happened to drop and reconnect on its own.
            // Republishing right here, immediately after a successful save, closes that gap — no
            // reconnect needed for a rename to actually take effect for everyone else.
            if (_messageTransport.IsConnected)
            {
                try { await _contactDirectoryService.PublishSelfAsync(); }
                catch { /* best-effort — the next reconnect's own PublishSelfAsync call is a safety net */ }
            }

            await ReconcileArimContactAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se uložit: {ex.Message}";
        }
    }

    /// <summary>
    /// 2026-10-08, user's own ask: reconcile this device's own profile against "Soukromé kontakty
    /// ARIM" on every Save. Match = formal name ("Příjmení Jméno", ARIM's own convention) OR phone
    /// OR email — any one hit counts. Deliberately NOT gated by RbacAction.EditContact (every role
    /// reconciles its own identity; that RBAC gate is only for manually editing OTHER people's
    /// contacts in the Kontakty tab). Ambiguous (>1 match) is deliberately left alone rather than
    /// guessing which one is "really" this device — rare enough (two ARIM rows sharing a phone/email)
    /// not to warrant its own UI.
    ///
    /// Uses <c>Shell.Current.CurrentPage.DisplayAlert</c> directly rather than this ViewModel's usual
    /// MAUI-free-with-an-event split (see this class's own remarks) — a deliberate scope call: adding
    /// a whole new event/page-code-behind round trip just for two confirm dialogs, for a ViewModel
    /// that already isn't fully MAUI-free (see OnIsLogbookVisibleChanged et al.), wasn't worth it here.
    /// </summary>
    private async Task ReconcileArimContactAsync()
    {
        ArimReconciliationStatusText = null;
        if (string.IsNullOrWhiteSpace(ProfileFirstName) || string.IsNullOrWhiteSpace(ProfileLastName))
            return;

        var formalName = $"{ProfileLastName.Trim()} {ProfileFirstName.Trim()}";
        try
        {
            var all = await _sharedContactService.FetchAsync();
            var arimMatches = all
                .Where(c => string.Equals(c.Note, "ARIM", StringComparison.Ordinal))
                .Where(c =>
                    string.Equals(c.DisplayName, formalName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(ProfilePhone) && string.Equals(c.Phone, ProfilePhone, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(ProfileEmail) && string.Equals(c.Email, ProfileEmail, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var page = Shell.Current?.CurrentPage;
            if (arimMatches.Count == 1)
            {
                var match = arimMatches[0];

                // Links this device to the matched contact regardless of whether anything else needs
                // fixing (2026-10-08, user's own ask: "je treba aby bylo jasno jaky uzivatel apku
                // pouziva... uz je na mobilu neni na pc" — an admin overview grouping devices by
                // person). Best-effort, same tolerance PublishSelfAsync's own callers already use.
                try { await _contactDirectoryService.LinkArimContactAsync(match.Id); } catch { /* best-effort */ }

                var alreadyInSync =
                    string.Equals(match.DisplayName, formalName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(match.Phone ?? string.Empty, ProfilePhone, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(match.Email ?? string.Empty, ProfileEmail, StringComparison.OrdinalIgnoreCase);
                if (alreadyInSync || page is null) return;

                var useMyProfile = await page.DisplayAlert(
                    "Neshoda s kontaktem ARIM",
                    $"Váš profil: {formalName}, {ProfilePhone}, {ProfileEmail}\nKontakt ARIM: {match.DisplayName}, {match.Phone}, {match.Email}\n\nKterý je správně?",
                    "Můj profil", "Kontakt ARIM");

                if (useMyProfile)
                {
                    var updated = match with { DisplayName = formalName, Phone = ProfilePhone, Email = ProfileEmail };
                    ArimReconciliationStatusText = await _sharedContactService.PublishAsync(updated)
                        ? "Kontakt ARIM aktualizován podle vašeho profilu."
                        : "Nepodařilo se aktualizovat kontakt ARIM.";
                }
                else
                {
                    // Splitting match.DisplayName back into First/Last reliably isn't safe (multi-word
                    // surnames etc.) — only Phone/Email, which ARE single unambiguous fields, get
                    // adopted locally. Jméno/Příjmení stay as the user typed them.
                    await _currentUserService.UpdateProfileAsync(null, null, match.Phone, match.Email);
                    ProfilePhone = match.Phone ?? string.Empty;
                    ProfileEmail = match.Email ?? string.Empty;
                    ArimReconciliationStatusText = "Telefon/e-mail převzaty z kontaktu ARIM.";
                }
            }
            else if (arimMatches.Count == 0 && page is not null)
            {
                var create = await page.DisplayAlert(
                    "Nenalezen kontakt ARIM",
                    $"Přidat se do Soukromých kontaktů ARIM jako '{formalName}'?",
                    "Přidat", "Ne");
                if (create)
                {
                    var created = new SharedContact(Guid.NewGuid(), formalName, ProfilePhone, "ARIM", 0, DateTimeOffset.UtcNow, ProfileEmail);
                    if (await _sharedContactService.PublishAsync(created))
                    {
                        ArimReconciliationStatusText = "Přidáno do Soukromých kontaktů ARIM.";
                        try { await _contactDirectoryService.LinkArimContactAsync(created.Id); } catch { /* best-effort */ }
                    }
                    else
                    {
                        ArimReconciliationStatusText = "Nepodařilo se přidat kontakt ARIM.";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ArimReconciliationStatusText = $"Porovnání s kontakty ARIM se nezdařilo: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RequestActivationAsync()
    {
        RelayErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            RelayErrorMessage = "Zadejte platnou adresu relay serveru, např. ws://10.8.0.1:8080";
            return;
        }
        if (string.IsNullOrWhiteSpace(ActivationEmailText))
        {
            RelayErrorMessage = "Zadejte svůj (nejlépe pracovní) e-mail.";
            return;
        }

        IsBusyWithRelay = true;
        try
        {
            _pendingActivationRequestId = await _messageTransport.RequestActivationAsync(endpoint, _currentUserService.Current.DisplayName, ActivationEmailText);
            // 2026-10-08, user's own ask: this is the one place a device's email is already typed
            // in locally (previously sent to the relay's activation_requests table but never kept
            // here) — persist it onto the profile now so ARIM reconciliation has it to match against
            // without needing any new relay round-trip.
            await _currentUserService.UpdateProfileAsync(null, null, null, ActivationEmailText);
            ProfileEmail = ActivationEmailText;
            ActivationStatusText = "Aktivace probíhá…";
            StartActivationPolling(endpoint);
        }
        catch (Exception ex)
        {
            RelayErrorMessage = $"Nepodařilo se odeslat žádost o aktivaci: {ex.Message}";
        }
        finally
        {
            IsBusyWithRelay = false;
        }
    }

    /// <summary>Ticks every few seconds until the admin decides — see <c>IMessageTransport.PollActivationAsync</c>'s remarks for why one call here does double duty as both "check status" and "finish registering" on Approved.</summary>
    private void StartActivationPolling(Uri endpoint)
    {
        if (_activationPollTimer is not null) return; // already running

        _activationPollTimer = Microsoft.Maui.Controls.Application.Current?.Dispatcher.CreateTimer();
        if (_activationPollTimer is null) return;

        _activationPollTimer.Interval = TimeSpan.FromSeconds(4);
        _activationPollTimer.Tick += (_, _) => _ = PollActivationOnceAsync(endpoint);
        _activationPollTimer.Start();
    }

    private async Task PollActivationOnceAsync(Uri endpoint)
    {
        if (_pendingActivationRequestId is not { } requestId)
            return;

        try
        {
            var status = await _messageTransport.PollActivationAsync(endpoint, requestId);
            switch (status)
            {
                case ActivationRequestStatus.Approved:
                    StopActivationPolling();
                    _pendingActivationRequestId = null;
                    IsRegistered = true;
                    ActivationStatusText = "Aktivováno — zařízení je zaregistrováno.";
                    break;
                case ActivationRequestStatus.Rejected:
                    StopActivationPolling();
                    _pendingActivationRequestId = null;
                    ActivationStatusText = "Žádost byla administrátorem zamítnuta.";
                    break;
                // Pending: nothing to do — the next tick just checks again.
            }
        }
        catch
        {
            // Best-effort — a transient network blip while polling shouldn't blow up the status
            // text or stop the loop; the next tick tries again on its own.
        }
    }

    /// <summary>Stops the poll loop — call when leaving the page (paired with the resume-on-reappear logic already in <see cref="LoadAsync"/>) so a background timer doesn't keep firing network calls for a page that isn't shown. Does not clear <see cref="_pendingActivationRequestId"/>/the persisted configuration — a still-pending request resumes polling next time LoadAsync runs.</summary>
    public void StopActivationPolling()
    {
        if (_activationPollTimer is null) return;
        _activationPollTimer.Stop();
        _activationPollTimer = null;
    }

    /// <summary>
    /// Reads the whole community's recent shared diagnostics log (2026-09-10) — unlike everything
    /// else this command sits next to, this genuinely needs no admin secret and no role check: it's
    /// device-authenticated the same way <c>/directory/members</c> already is, since letting an AI
    /// assistant (or any operator) see what's actually failing across every device, from just ONE
    /// device's Settings page, is the entire point (see IDiagnosticsReporter's own remarks).
    /// </summary>
    [RelayCommand]
    private async Task LoadDiagnosticLogAsync()
    {
        DiagnosticLogErrorMessage = null;
        IsLoadingDiagnosticLog = true;
        try
        {
            var entries = await _diagnosticsReporter.GetRecentAsync();
            DiagnosticLogEntries = new ObservableCollection<DiagnosticLogItem>(entries.Select(ToDiagnosticLogItem));
            HasNoDiagnosticLogEntries = DiagnosticLogEntries.Count == 0;
        }
        catch (Exception ex)
        {
            DiagnosticLogErrorMessage = $"Nepodařilo se načíst diagnostický log: {ex.Message}";
        }
        finally
        {
            IsLoadingDiagnosticLog = false;
        }
    }

    private static DiagnosticLogItem ToDiagnosticLogItem(DiagnosticLogEntry entry) => new(
        entry.CreatedAtUtc.LocalDateTime.ToString("g"),
        entry.Level.ToString(),
        entry.DeviceDisplayName,
        entry.Message,
        entry.Context);

    [RelayCommand]
    private async Task ConnectToRelayAsync()
    {
        RelayErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            RelayErrorMessage = "Zadejte platnou adresu relay serveru, např. ws://10.8.0.1:8080";
            return;
        }

        IsBusyWithRelay = true;
        try
        {
            await _messageTransport.ConnectAsync(endpoint);
            IsConnected = _messageTransport.IsConnected;
            ConnectionStatusText = IsConnected ? "Připojeno" : "Odpojeno";
        }
        catch (Exception ex)
        {
            RelayErrorMessage = $"Nepodařilo se připojit: {ex.Message}";
        }
        finally
        {
            IsBusyWithRelay = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectFromRelayAsync()
    {
        await _messageTransport.DisconnectAsync();
        IsConnected = _messageTransport.IsConnected;
        ConnectionStatusText = "Odpojeno";
    }

    /// <summary>
    /// Call from the page's OnAppearing (paired with <see cref="StopObservingConnection"/> in
    /// OnDisappearing) — subscribes to a Singleton service's event, so it must be unsubscribed or
    /// every page visit leaks a handler (this ViewModel is Transient, re-created per navigation).
    /// </summary>
    public void StartObservingConnection()
    {
        if (_connectionStateHandler is not null) return;

        _connectionStateHandler = (_, state) => MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = state == TransportConnectionState.Connected;
            ConnectionStatusText = DescribeConnectionState(state);
        });
        _messageTransport.ConnectionStateChanged += _connectionStateHandler;
    }

    public void StopObservingConnection()
    {
        if (_connectionStateHandler is null) return;
        _messageTransport.ConnectionStateChanged -= _connectionStateHandler;
        _connectionStateHandler = null;
    }

    /// <summary>Czech display text for <see cref="TransportConnectionState"/> — the enum itself stays English (it's a wire/internal concept), only what reaches ConnectionStatusText is translated.</summary>
    private static string DescribeConnectionState(TransportConnectionState state) => state switch
    {
        TransportConnectionState.Connected => "Připojeno",
        TransportConnectionState.Connecting => "Připojování…",
        TransportConnectionState.Reconnecting => "Připojování znovu…",
        _ => "Odpojeno"
    };
}

/// <summary>One row in the admin's device-management list (2.1, 2026-09-17) — <see cref="StatusText"/>/<see cref="PendingText"/> are pre-formatted here (not in XAML) so the CollectionView's DataTemplate needs no value converters, this codebase's established "no converters" convention.</summary>
public sealed record RegisteredDeviceItem(Guid Id, string DisplayName, string StatusText, string PendingText, bool HasPendingMessages, bool IsStale, ICommand DeregisterCommand, ICommand ShowErrorsCommand, ICommand ShowEventsCommand)
{
    /// <summary>Person header (2026-10-09), set only on the first device row of each person — see <c>SettingsViewModel.GroupDevicesByPerson</c>.</summary>
    public string? GroupHeader { get; init; }
    public string? GroupCoverageText { get; init; }
    public bool HasGroupHeader => GroupHeader is not null;
}

/// <summary>One row in the "Diagnostický log" list (2026-09-10) — display-only, no per-row command, unlike <see cref="RegisteredDeviceItem"/>. <see cref="Level"/> stays the English enum name (<c>Error</c>/<c>Warning</c>/<c>Info</c>) — the XAML template colors it, doesn't translate it, same "wire-level concept stays English" call this codebase already made for <c>TransportConnectionState</c>. <see cref="HasContext"/> is precomputed here (not a converter) so the DataTemplate's <c>IsVisible</c> binding stays a plain bool — this codebase's established preference over introducing a new <c>IValueConverter</c> for one spot.</summary>
public sealed record DiagnosticLogItem(string TimeText, string Level, string DeviceDisplayName, string Message, string? Context)
{
    public bool HasContext => !string.IsNullOrEmpty(Context);
}
