using System.Collections.ObjectModel;
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
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IDiagnosticsReporter _diagnosticsReporter;

    private EventHandler<TransportConnectionState>? _connectionStateHandler;
    private IDispatcherTimer? _activationPollTimer;
    private Guid? _pendingActivationRequestId;

    public IReadOnlyList<Role> AvailableRoles { get; } = Enum.GetValues<Role>();

    [ObservableProperty]
    public partial string DisplayName { get; set; }

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

    // --- Join by invite code (2026-09-10) — the original pre-2026-09-06 registration path, kept
    // working the whole time but unreachable from this UI until now: the user's own follow-up
    // after asking how to let someone install the app at all — "zatím bez nutnosti zadávat email...
    // jak jsi to vyřešil s těmi kódy?" (for now without needing an email — how did the old codes
    // work?). A genuine second onboarding path alongside Aktivovat above, not a replacement: no
    // email collected, no admin approval step to wait on — the code itself, shared out-of-band by
    // whoever generated it (see GenerateInviteCommand below), is the only credential needed.
    // IMessageTransport.RegisterAsync/IRelayAdminService.CreateInviteAsync were never removed when
    // the activation-request flow superseded this in the UI, exactly so this stayed possible later.

    [ObservableProperty]
    public partial string JoinInviteCodeText { get; set; }

    [ObservableProperty]
    public partial string? GeneratedInviteCode { get; set; }

    [ObservableProperty]
    public partial bool HasGeneratedInviteCode { get; set; }

    [ObservableProperty]
    public partial string? GeneratedInviteExpiryText { get; set; }

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

    [ObservableProperty]
    public partial string ContactCardText { get; set; }

    /// <summary>The same contact card, packed via QrBlobCodec for the "Show QR" flow — see that class's own remarks for why it can't just reuse ContactCardText's copy/paste format.</summary>
    [ObservableProperty]
    public partial string ContactCardQrValue { get; set; }

    [ObservableProperty]
    public partial bool IsShowingContactCardQr { get; set; }

    // --- Shared library key (Milestone 5 follow-up) ---

    [ObservableProperty]
    public partial bool HasSharedLibraryKey { get; set; }

    /// <summary>Mirrors <see cref="HasSharedLibraryKey"/> — kept as its own bound property (this codebase's established pattern, see HasNoAdminSecret) so XAML never needs to negate a binding. Gates which of Generate/Show is offered, so a key that already exists can only be re-shown, never silently regenerated and desynced from the rest of the community.</summary>
    [ObservableProperty]
    public partial bool HasNoSharedLibraryKey { get; set; }

    [ObservableProperty]
    public partial string? SharedLibraryKeyBlob { get; set; }

    [ObservableProperty]
    public partial bool HasSharedLibraryKeyBlob { get; set; }

    [ObservableProperty]
    public partial string SharedLibraryKeyImportText { get; set; }

    [ObservableProperty]
    public partial string? SharedLibraryErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasSharedLibraryError { get; set; }

    /// <summary>Status text for <see cref="ResendSharedLibraryKeyAsync"/> (2026-09-11) — the user's own explicit ask after the automatic per-chat-open offer still wasn't reliably reaching every already-paired peer: a manual "resend to everyone" action with visible confirmation of how many sessions got it.</summary>
    [ObservableProperty]
    public partial string? SharedLibraryBroadcastStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasSharedLibraryBroadcastStatus { get; set; }

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
        IDiagnosticsReporter diagnosticsReporter,
        IChatSessionRepository chatSessionRepository)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
        _relayAdminService = relayAdminService ?? throw new ArgumentNullException(nameof(relayAdminService));
        _contactDirectoryService = contactDirectoryService ?? throw new ArgumentNullException(nameof(contactDirectoryService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));

        DiagnosticLogEntries = [];
        HasNoDiagnosticLogEntries = true;
        DisplayName = string.Empty;
        RelayEndpointText = string.Empty;
        ActivationEmailText = string.Empty;
        JoinInviteCodeText = string.Empty;
        ConnectionStatusText = "Odpojeno";
        IsNotConnected = true;
        IsNotRegistered = true;
        CanUseRelayControls = true;
        ContactCardText = string.Empty;
        ContactCardQrValue = string.Empty;
        SharedLibraryKeyImportText = string.Empty;
        AdminSecretInputText = string.Empty;
        CanUseAdminControls = true;
        HasNoSharedLibraryKey = true;
        RegisteredDevices = [];
        HasNoRegisteredDevices = true;
    }

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);

    partial void OnDisplayNameChanged(string value) => IsSaved = false;

    partial void OnSelectedRoleChanged(Role value)
    {
        IsSaved = false;
        IsAdmin = value == Role.Admin;
    }

    partial void OnRelayErrorMessageChanged(string? value) => HasRelayError = !string.IsNullOrEmpty(value);

    partial void OnActivationStatusTextChanged(string? value) => HasActivationStatus = !string.IsNullOrEmpty(value);

    partial void OnGeneratedInviteCodeChanged(string? value) => HasGeneratedInviteCode = !string.IsNullOrEmpty(value);

    partial void OnIsBusyWithRelayChanged(bool value) => CanUseRelayControls = !value;

    partial void OnIsConnectedChanged(bool value) => IsNotConnected = !value;

    partial void OnIsRegisteredChanged(bool value) => IsNotRegistered = !value;

    partial void OnSharedLibraryErrorMessageChanged(string? value) => HasSharedLibraryError = !string.IsNullOrEmpty(value);

    partial void OnSharedLibraryKeyBlobChanged(string? value) => HasSharedLibraryKeyBlob = !string.IsNullOrEmpty(value);

    partial void OnHasSharedLibraryKeyChanged(bool value) => HasNoSharedLibraryKey = !value;

    partial void OnSharedLibraryBroadcastStatusTextChanged(string? value) => HasSharedLibraryBroadcastStatus = !string.IsNullOrEmpty(value);

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

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _currentUserService.InitializeAsync();
        DisplayName = _currentUserService.Current.DisplayName;
        SelectedRole = _currentUserService.Current.Role;
        IsAdmin = SelectedRole == Role.Admin;
        IsSaved = false;
        ErrorMessage = null;
        IsLogbookVisible = Preferences.Default.Get(AppShell.LogbookVisibilityPreferenceKey, false);
        IsChatsTabVisible = Preferences.Default.Get(AppShell.ChatsTabVisibilityPreferenceKey, true);
        IsFilesTabVisible = Preferences.Default.Get(AppShell.FilesTabVisibilityPreferenceKey, true);
        IsContactsTabVisible = Preferences.Default.Get(AppShell.ContactsTabVisibilityPreferenceKey, true);

        var configuration = await _transportSettingsRepository.GetAsync();
        // No saved endpoint yet (first time this device opens Settings) -> pre-fill the
        // community's one relay address instead of leaving the field blank for the user to guess.
        RelayEndpointText = configuration?.EndpointUri?.ToString() ?? RelayDefaults.DefaultEndpoint;
        IsRegistered = configuration?.AssignedDeviceId is not null;
        IsConnected = _messageTransport.IsConnected;
        ConnectionStatusText = IsConnected ? "Připojeno" : "Odpojeno";

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

        await RefreshContactCardAsync();

        HasSharedLibraryKey = await _sharedLibraryService.HasSharedKeyAsync();

        // Diagnostic log auto-loads here since it needs no admin secret — LoadDiagnosticLogAsync
        // already catches its own failures into DiagnosticLogErrorMessage rather than throwing, so
        // a relay that's unreachable right now doesn't block the rest of this page from loading;
        // "⟳ Obnovit" retries it explicitly.
        await LoadDiagnosticLogAsync();
    }

    [RelayCommand]
    private void ToggleContactCardQr() => IsShowingContactCardQr = !IsShowingContactCardQr;

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
        await RefreshRegisteredDevicesAsync(endpoint, adminSecret);
    }

    private async Task RefreshRegisteredDevicesAsync(Uri endpoint, string adminSecret)
    {
        IsLoadingDevices = true;
        try
        {
            var devices = await _relayAdminService.GetRegisteredDevicesAsync(endpoint, adminSecret);
            RegisteredDevices = new ObservableCollection<RegisteredDeviceItem>(devices.Select(ToRegisteredDeviceItem));
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
            device.DirectoryDisplayName ?? device.DisplayName,
            statusText,
            pendingText,
            device.PendingOutboxCount > 0,
            device.LastActiveAtUtc is null,
            DeregisterDeviceCommand);
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
    private async Task GenerateSharedLibraryKeyAsync()
    {
        SharedLibraryErrorMessage = null;
        try
        {
            SharedLibraryKeyBlob = await _sharedLibraryService.GenerateSharedKeyAsync();
            HasSharedLibraryKey = true;

            // Push the new key straight out to everyone already paired (2026-09-11) — see
            // SharedLibraryKeySync.BroadcastToAllActiveSessionsAsync's own remarks. Best-effort: a
            // brand-new key with nobody paired yet is the expected common case on first setup, not
            // an error.
            await BroadcastSharedLibraryKeyAsync();
        }
        catch (Exception ex)
        {
            SharedLibraryErrorMessage = $"Nepodařilo se vygenerovat klíč: {ex.Message}";
        }
    }

    /// <summary>Re-shows the already-stored key (e.g. after the blob was dismissed/the app restarted before another device imported it) without minting a new one — see ISharedLibraryService.ExportSharedKeyAsync's own remarks for why that distinction matters.</summary>
    [RelayCommand]
    private async Task ShowSharedLibraryKeyAsync()
    {
        SharedLibraryErrorMessage = null;
        try
        {
            SharedLibraryKeyBlob = await _sharedLibraryService.ExportSharedKeyAsync();
        }
        catch (Exception ex)
        {
            SharedLibraryErrorMessage = $"Nepodařilo se načíst uložený klíč: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportSharedLibraryKeyAsync()
    {
        SharedLibraryErrorMessage = null;
        if (string.IsNullOrWhiteSpace(SharedLibraryKeyImportText))
        {
            SharedLibraryErrorMessage = "Nejprve vložte klíč vygenerovaný někým jiným.";
            return;
        }

        try
        {
            await _sharedLibraryService.ImportSharedKeyAsync(SharedLibraryKeyImportText);
            HasSharedLibraryKey = true;
            SharedLibraryKeyImportText = string.Empty;

            // Same reasoning as GenerateSharedLibraryKeyAsync's own call — a freshly-imported key is
            // exactly as worth re-broadcasting as a freshly-generated one (e.g. this device missed
            // the original round and someone re-shared the blob manually one more time).
            await BroadcastSharedLibraryKeyAsync();
        }
        catch (Exception ex)
        {
            SharedLibraryErrorMessage = $"Nepodařilo se importovat tento klíč: {ex.Message}";
        }
    }

    /// <summary>
    /// Immediately pushes a just-generated/imported key to everyone this device is already paired
    /// with (2026-09-11) — the automatic distribution's one event-driven trigger, on top of the
    /// connection supervisor's periodic push/pull sweep (see <c>SharedLibraryKeySync.AutoSyncAsync</c>).
    /// Deliberately NOT a user-facing button: the user was explicit that passing keys around must
    /// never be the user's job ("to ma udelat aplikace sama") — this just makes the app react the
    /// instant a key exists instead of waiting up to one sweep interval.
    /// </summary>
    private async Task BroadcastSharedLibraryKeyAsync()
    {
        try
        {
            // Primary: escrow the key on the relay wrapped for every member (robust, reaches even
            // offline members when they next connect — see PublishWrappedKeyForMembersAsync).
            await _sharedLibraryService.PublishWrappedKeyForMembersAsync();

            // Secondary: also offer it live over any already-paired session (instant for online peers).
            var offeredCount = await SharedLibraryKeySync.BroadcastToAllActiveSessionsAsync(
                _sharedLibraryService, _messagingService, _messageTransport, _chatSessionRepository, _diagnosticsReporter);
            SharedLibraryBroadcastStatusText = "Klíč byl automaticky rozeslán ostatním zařízením v komunitě.";
        }
        catch (Exception ex)
        {
            SharedLibraryBroadcastStatusText = $"Automatické rozeslání klíče se zatím nezdařilo (zkusí se znovu na pozadí): {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        try
        {
            await _currentUserService.SetCurrentUserAsync(DisplayName, SelectedRole);
            IsSaved = true;
            await RefreshContactCardAsync(); // display name is embedded in the contact card

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
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se uložit: {ex.Message}";
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

    /// <summary>
    /// The code-based alternative to <see cref="RequestActivationAsync"/> above (2026-09-10) — see
    /// <see cref="JoinInviteCodeText"/>'s own remarks. Registers immediately, no admin approval to
    /// wait for: whoever generated the code (<see cref="GenerateInviteAsync"/> below) already made
    /// the trust decision by choosing to hand it out.
    /// </summary>
    [RelayCommand]
    private async Task RegisterWithCodeAsync()
    {
        RelayErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            RelayErrorMessage = "Zadejte platnou adresu relay serveru, např. ws://10.8.0.1:8080";
            return;
        }
        if (string.IsNullOrWhiteSpace(JoinInviteCodeText))
        {
            RelayErrorMessage = "Zadejte kód pozvánky.";
            return;
        }

        IsBusyWithRelay = true;
        try
        {
            await _currentUserService.InitializeAsync();
            await _messageTransport.RegisterAsync(endpoint, JoinInviteCodeText.Trim(), _currentUserService.Current.DisplayName);
            IsRegistered = true;
            JoinInviteCodeText = string.Empty;
            await RefreshContactCardAsync();
        }
        catch (Exception ex)
        {
            RelayErrorMessage = $"Registrace kódem se nezdařila: {ex.Message}";
        }
        finally
        {
            IsBusyWithRelay = false;
        }
    }

    /// <summary>
    /// Admin-only counterpart to <see cref="RegisterWithCodeAsync"/> — mints the code a new device
    /// pastes there. Same "type the admin secret fresh for this one action" pattern every other
    /// admin command on this page already uses (<see cref="TryTakeAdminSecret"/>'s own remarks).
    /// Fixed 60-minute validity — no UI field for it, kept deliberately simple.
    /// </summary>
    [RelayCommand]
    private async Task GenerateInviteAsync()
    {
        AdminErrorMessage = null;
        GeneratedInviteCode = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }
        if (!TryTakeAdminSecret(out var adminSecret)) return;

        IsBusyWithAdmin = true;
        try
        {
            var (code, expiresAtUtc) = await _relayAdminService.CreateInviteAsync(endpoint, adminSecret, displayNameHint: null, validForMinutes: 60);
            GeneratedInviteCode = code;
            GeneratedInviteExpiryText = $"Platí do {expiresAtUtc.LocalDateTime:g}";
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se vygenerovat kód: {ex.Message}";
        }
        finally
        {
            IsBusyWithAdmin = false;
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
                    await RefreshContactCardAsync();
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

    private async Task RefreshContactCardAsync()
    {
        try
        {
            var configuration = await _transportSettingsRepository.GetAsync();
            if (configuration?.AssignedDeviceId is not { } deviceId)
            {
                ContactCardText = string.Empty;
                ContactCardQrValue = string.Empty;
                return;
            }

            var publicKey = await _messagingService.GetLocalIdentityPublicKeyAsync();
            var card = new ContactCardBlob(_currentUserService.Current.DisplayName, publicKey, deviceId);
            ContactCardText = ContactCardCodec.Encode(card);
            ContactCardQrValue = QrBlobCodec.EncodeContactCard(card);
        }
        catch (Exception)
        {
            ContactCardText = string.Empty;
            ContactCardQrValue = string.Empty;
        }
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
public sealed record RegisteredDeviceItem(Guid Id, string DisplayName, string StatusText, string PendingText, bool HasPendingMessages, bool IsStale, ICommand DeregisterCommand);

/// <summary>One row in the "Diagnostický log" list (2026-09-10) — display-only, no per-row command, unlike <see cref="RegisteredDeviceItem"/>. <see cref="Level"/> stays the English enum name (<c>Error</c>/<c>Warning</c>/<c>Info</c>) — the XAML template colors it, doesn't translate it, same "wire-level concept stays English" call this codebase already made for <c>TransportConnectionState</c>. <see cref="HasContext"/> is precomputed here (not a converter) so the DataTemplate's <c>IsVisible</c> binding stays a plain bool — this codebase's established preference over introducing a new <c>IValueConverter</c> for one spot.</summary>
public sealed record DiagnosticLogItem(string TimeText, string Level, string DeviceDisplayName, string Message, string? Context)
{
    public bool HasContext => !string.IsNullOrEmpty(Context);
}
