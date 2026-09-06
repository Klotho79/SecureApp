using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Chat;
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

    // --- Relay (Milestone 5) ---

    [ObservableProperty]
    public partial string RelayEndpointText { get; set; }

    // --- Activation (2026-09-06) — replaces the invite-code paste-in below for a new device
    // joining the community: instead of typing in a code an admin handed over out-of-band, the new
    // device sends the admin a request (name + email + this device's own key fingerprint) and just
    // waits for it to be approved in-app. See IMessageTransport.RequestActivationAsync/PollActivationAsync
    // and the "Admin: Pending Activations" properties further below for the admin's side of this.

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

    // --- Relay admin: approve/reject device activation requests in-app (Admin role only) ---

    [ObservableProperty]
    public partial bool HasAdminSecret { get; set; }

    [ObservableProperty]
    public partial bool HasNoAdminSecret { get; set; }

    [ObservableProperty]
    public partial string AdminSecretInputText { get; set; }

    /// <summary>Every activation request still awaiting a decision, newest-request-command already baked in as <see cref="PendingActivationItem.ApproveCommand"/>/<see cref="PendingActivationItem.RejectCommand"/> so the CollectionView's DataTemplate needs no <c>x:Reference</c> back to this page — same pattern this codebase already used for chat-attachment/category-chip rows before those were simplified away.</summary>
    [ObservableProperty]
    public partial ObservableCollection<PendingActivationItem> PendingActivations { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingActivations { get; set; }

    [ObservableProperty]
    public partial bool HasNoPendingActivations { get; set; }

    [ObservableProperty]
    public partial bool HasPendingActivations { get; set; }

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

    public SettingsViewModel(
        ICurrentUserService currentUserService,
        IMessagingService messagingService,
        ITransportSettingsRepository transportSettingsRepository,
        IMessageTransport messageTransport,
        ISharedLibraryService sharedLibraryService,
        IRelayAdminService relayAdminService)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
        _relayAdminService = relayAdminService ?? throw new ArgumentNullException(nameof(relayAdminService));

        DisplayName = string.Empty;
        RelayEndpointText = string.Empty;
        ActivationEmailText = string.Empty;
        ConnectionStatusText = "Odpojeno";
        IsNotConnected = true;
        IsNotRegistered = true;
        CanUseRelayControls = true;
        ContactCardText = string.Empty;
        ContactCardQrValue = string.Empty;
        SharedLibraryKeyImportText = string.Empty;
        AdminSecretInputText = string.Empty;
        HasNoAdminSecret = true;
        CanUseAdminControls = true;
        HasNoSharedLibraryKey = true;
        PendingActivations = [];
        HasNoPendingActivations = true;
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

    partial void OnIsBusyWithRelayChanged(bool value) => CanUseRelayControls = !value;

    partial void OnIsConnectedChanged(bool value) => IsNotConnected = !value;

    partial void OnIsRegisteredChanged(bool value) => IsNotRegistered = !value;

    partial void OnSharedLibraryErrorMessageChanged(string? value) => HasSharedLibraryError = !string.IsNullOrEmpty(value);

    partial void OnSharedLibraryKeyBlobChanged(string? value) => HasSharedLibraryKeyBlob = !string.IsNullOrEmpty(value);

    partial void OnHasSharedLibraryKeyChanged(bool value) => HasNoSharedLibraryKey = !value;

    partial void OnAdminErrorMessageChanged(string? value) => HasAdminError = !string.IsNullOrEmpty(value);

    partial void OnHasAdminSecretChanged(bool value) => HasNoAdminSecret = !value;

    partial void OnIsBusyWithAdminChanged(bool value) => CanUseAdminControls = !value;

    partial void OnDeployStatusTextChanged(string? value) => HasDeployStatus = !string.IsNullOrEmpty(value);

    partial void OnHasNoPendingActivationsChanged(bool value) => HasPendingActivations = !value;

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _currentUserService.InitializeAsync();
        DisplayName = _currentUserService.Current.DisplayName;
        SelectedRole = _currentUserService.Current.Role;
        IsAdmin = SelectedRole == Role.Admin;
        IsSaved = false;
        ErrorMessage = null;

        var configuration = await _transportSettingsRepository.GetAsync();
        // No saved endpoint yet (first time this device opens Settings) -> pre-fill the
        // community's one relay address instead of leaving the field blank for the user to guess.
        RelayEndpointText = configuration?.EndpointUri?.ToString() ?? RelayDefaults.DefaultEndpoint;
        IsRegistered = configuration?.AssignedDeviceId is not null;
        IsConnected = _messageTransport.IsConnected;
        ConnectionStatusText = IsConnected ? "Připojeno" : "Odpojeno";

        // Resume polling an activation request that was still pending the last time this device
        // closed — otherwise a relaunch between "Aktivovat" and the admin's decision would silently
        // strand the request: nothing would ever check on it again even after the admin approves.
        if (!IsRegistered && configuration?.PendingActivationRequestId is { } pendingRequestId
            && Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var resumedEndpoint))
        {
            _pendingActivationRequestId = pendingRequestId;
            ActivationStatusText = "Čeká na schválení administrátorem…";
            StartActivationPolling(resumedEndpoint);
        }

        await RefreshContactCardAsync();

        HasSharedLibraryKey = await _sharedLibraryService.HasSharedKeyAsync();
        HasAdminSecret = await _relayAdminService.HasAdminSecretAsync();

        if (IsAdmin && HasAdminSecret)
            await LoadPendingActivationsAsync();
    }

    [RelayCommand]
    private async Task SaveAdminSecretAsync()
    {
        AdminErrorMessage = null;
        if (string.IsNullOrWhiteSpace(AdminSecretInputText))
        {
            AdminErrorMessage = "Nejprve zadejte SECUREAPP_RELAY_ADMIN_SECRET relay serveru.";
            return;
        }

        try
        {
            await _relayAdminService.SetAdminSecretAsync(AdminSecretInputText);
            HasAdminSecret = true;
            AdminSecretInputText = string.Empty;
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se uložit admin heslo: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleContactCardQr() => IsShowingContactCardQr = !IsShowingContactCardQr;

    /// <summary>Lets the user re-enter the admin secret (e.g. after a typo) without needing to know it was even wrong — just shows the input field again; the next Save overwrites whatever was stored before.</summary>
    [RelayCommand]
    private void ChangeAdminSecret()
    {
        AdminErrorMessage = null;
        HasAdminSecret = false;
    }

    [RelayCommand]
    private async Task LoadPendingActivationsAsync()
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }

        IsLoadingActivations = true;
        try
        {
            var pending = await _relayAdminService.GetPendingActivationRequestsAsync(endpoint);
            PendingActivations = new ObservableCollection<PendingActivationItem>(pending.Select(ToPendingActivationItem));
            HasNoPendingActivations = PendingActivations.Count == 0;
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se načíst čekající žádosti: {ex.Message}";
        }
        finally
        {
            IsLoadingActivations = false;
        }
    }

    private PendingActivationItem ToPendingActivationItem(PendingActivationRequest request) => new(
        request.Id,
        request.DisplayName,
        request.Email,
        request.KeyFingerprint,
        request.CreatedAtUtc.LocalDateTime.ToString("g"),
        ApproveActivationCommand,
        RejectActivationCommand);

    [RelayCommand]
    private async Task ApproveActivationAsync(Guid id)
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }

        try
        {
            await _relayAdminService.ApproveActivationRequestAsync(endpoint, id);
            await LoadPendingActivationsAsync();
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se schválit žádost: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RejectActivationAsync(Guid id)
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }

        try
        {
            await _relayAdminService.RejectActivationRequestAsync(endpoint, id);
            await LoadPendingActivationsAsync();
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Nepodařilo se zamítnout žádost: {ex.Message}";
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

        IsBusyWithAdmin = true;
        try
        {
            await _relayAdminService.RequestDeployAsync(endpoint);
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
        }
        catch (Exception ex)
        {
            SharedLibraryErrorMessage = $"Nepodařilo se importovat tento klíč: {ex.Message}";
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
            ActivationStatusText = "Čeká na schválení administrátorem…";
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

/// <summary>One row in the admin's "Čekající aktivace" list — carries the same shared Approve/Reject <see cref="RelayCommand{T}"/> instances (bound per-item as <c>CommandParameter="{Binding Id}"</c> in the DataTemplate) rather than an <c>x:Reference</c> back to the page.</summary>
public sealed record PendingActivationItem(Guid Id, string DisplayName, string Email, string KeyFingerprint, string CreatedText, ICommand ApproveCommand, ICommand RejectCommand);
