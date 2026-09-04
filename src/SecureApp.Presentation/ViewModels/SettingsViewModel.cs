using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
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

    [ObservableProperty]
    public partial string InviteCodeText { get; set; }

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

    // --- Shared library key (Milestone 5 follow-up) ---

    [ObservableProperty]
    public partial bool HasSharedLibraryKey { get; set; }

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

    // --- Relay admin: mint invite codes in-app (Admin role only) ---

    [ObservableProperty]
    public partial bool HasAdminSecret { get; set; }

    [ObservableProperty]
    public partial bool HasNoAdminSecret { get; set; }

    [ObservableProperty]
    public partial string AdminSecretInputText { get; set; }

    [ObservableProperty]
    public partial string InviteDisplayNameHintText { get; set; }

    [ObservableProperty]
    public partial string? GeneratedInviteCodeText { get; set; }

    [ObservableProperty]
    public partial bool HasGeneratedInviteCode { get; set; }

    [ObservableProperty]
    public partial string? GeneratedInviteExpiryText { get; set; }

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
        InviteCodeText = string.Empty;
        ConnectionStatusText = "Disconnected";
        IsNotConnected = true;
        IsNotRegistered = true;
        CanUseRelayControls = true;
        ContactCardText = string.Empty;
        SharedLibraryKeyImportText = string.Empty;
        AdminSecretInputText = string.Empty;
        InviteDisplayNameHintText = string.Empty;
        HasNoAdminSecret = true;
        CanUseAdminControls = true;
    }

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);

    partial void OnDisplayNameChanged(string value) => IsSaved = false;

    partial void OnSelectedRoleChanged(Role value)
    {
        IsSaved = false;
        IsAdmin = value == Role.Admin;
    }

    partial void OnRelayErrorMessageChanged(string? value) => HasRelayError = !string.IsNullOrEmpty(value);

    partial void OnIsBusyWithRelayChanged(bool value) => CanUseRelayControls = !value;

    partial void OnIsConnectedChanged(bool value) => IsNotConnected = !value;

    partial void OnIsRegisteredChanged(bool value) => IsNotRegistered = !value;

    partial void OnSharedLibraryErrorMessageChanged(string? value) => HasSharedLibraryError = !string.IsNullOrEmpty(value);

    partial void OnSharedLibraryKeyBlobChanged(string? value) => HasSharedLibraryKeyBlob = !string.IsNullOrEmpty(value);

    partial void OnAdminErrorMessageChanged(string? value) => HasAdminError = !string.IsNullOrEmpty(value);

    partial void OnHasAdminSecretChanged(bool value) => HasNoAdminSecret = !value;

    partial void OnIsBusyWithAdminChanged(bool value) => CanUseAdminControls = !value;

    partial void OnGeneratedInviteCodeTextChanged(string? value) => HasGeneratedInviteCode = !string.IsNullOrEmpty(value);

    partial void OnDeployStatusTextChanged(string? value) => HasDeployStatus = !string.IsNullOrEmpty(value);

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
        ConnectionStatusText = IsConnected ? "Connected" : "Disconnected";

        await RefreshContactCardAsync();

        HasSharedLibraryKey = await _sharedLibraryService.HasSharedKeyAsync();
        HasAdminSecret = await _relayAdminService.HasAdminSecretAsync();
    }

    [RelayCommand]
    private async Task SaveAdminSecretAsync()
    {
        AdminErrorMessage = null;
        if (string.IsNullOrWhiteSpace(AdminSecretInputText))
        {
            AdminErrorMessage = "Enter the relay's SECUREAPP_RELAY_ADMIN_SECRET first.";
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
            AdminErrorMessage = $"Could not save the admin secret: {ex.Message}";
        }
    }

    /// <summary>Lets the user re-enter the admin secret (e.g. after a typo) without needing to know it was even wrong — just shows the input field again; the next Save overwrites whatever was stored before.</summary>
    [RelayCommand]
    private void ChangeAdminSecret()
    {
        AdminErrorMessage = null;
        HasAdminSecret = false;
    }

    [RelayCommand]
    private async Task GenerateInviteAsync()
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Enter a valid relay address above first.";
            return;
        }

        IsBusyWithAdmin = true;
        try
        {
            var hint = string.IsNullOrWhiteSpace(InviteDisplayNameHintText) ? null : InviteDisplayNameHintText;
            var (inviteCode, expiresAtUtc) = await _relayAdminService.CreateInviteAsync(endpoint, hint, validForMinutes: 60);
            GeneratedInviteCodeText = inviteCode;
            GeneratedInviteExpiryText = $"Expires {expiresAtUtc.ToLocalTime():g}";
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Could not generate an invite code: {ex.Message}";
        }
        finally
        {
            IsBusyWithAdmin = false;
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
            AdminErrorMessage = "Enter a valid relay address above first.";
            return;
        }

        IsBusyWithAdmin = true;
        try
        {
            await _relayAdminService.RequestDeployAsync(endpoint);
            DeployStatusText = "Redeploy requested — the relay will rebuild and restart within a few seconds.";
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Could not request a redeploy: {ex.Message}";
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
            SharedLibraryErrorMessage = $"Could not generate a key: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportSharedLibraryKeyAsync()
    {
        SharedLibraryErrorMessage = null;
        if (string.IsNullOrWhiteSpace(SharedLibraryKeyImportText))
        {
            SharedLibraryErrorMessage = "Paste the key someone else generated first.";
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
            SharedLibraryErrorMessage = $"Could not import that key: {ex.Message}";
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
            ErrorMessage = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RegisterWithRelayAsync()
    {
        RelayErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            RelayErrorMessage = "Enter a valid relay address, e.g. ws://10.8.0.1:8080";
            return;
        }
        if (string.IsNullOrWhiteSpace(InviteCodeText))
        {
            RelayErrorMessage = "Enter the invite code you were given.";
            return;
        }

        IsBusyWithRelay = true;
        try
        {
            await _messageTransport.RegisterAsync(endpoint, InviteCodeText, _currentUserService.Current.DisplayName);
            IsRegistered = true;
            await RefreshContactCardAsync();
        }
        catch (Exception ex)
        {
            RelayErrorMessage = $"Registration failed: {ex.Message}";
        }
        finally
        {
            IsBusyWithRelay = false;
        }
    }

    [RelayCommand]
    private async Task ConnectToRelayAsync()
    {
        RelayErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            RelayErrorMessage = "Enter a valid relay address, e.g. ws://10.8.0.1:8080";
            return;
        }

        IsBusyWithRelay = true;
        try
        {
            await _messageTransport.ConnectAsync(endpoint);
            IsConnected = _messageTransport.IsConnected;
            ConnectionStatusText = IsConnected ? "Connected" : "Disconnected";
        }
        catch (Exception ex)
        {
            RelayErrorMessage = $"Could not connect: {ex.Message}";
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
        ConnectionStatusText = "Disconnected";
    }

    private async Task RefreshContactCardAsync()
    {
        try
        {
            var configuration = await _transportSettingsRepository.GetAsync();
            if (configuration?.AssignedDeviceId is not { } deviceId)
            {
                ContactCardText = string.Empty;
                return;
            }

            var publicKey = await _messagingService.GetLocalIdentityPublicKeyAsync();
            var card = new ContactCardBlob(_currentUserService.Current.DisplayName, publicKey, deviceId);
            ContactCardText = ContactCardCodec.Encode(card);
        }
        catch (Exception)
        {
            ContactCardText = string.Empty;
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
            ConnectionStatusText = state.ToString();
        });
        _messageTransport.ConnectionStateChanged += _connectionStateHandler;
    }

    public void StopObservingConnection()
    {
        if (_connectionStateHandler is null) return;
        _messageTransport.ConnectionStateChanged -= _connectionStateHandler;
        _connectionStateHandler = null;
    }
}
