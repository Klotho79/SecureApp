using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Chat;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Pairs a new chat session without any QR/contact-exchange UI yet — the user pastes a peer's
/// "contact card" (display name + identity public key + relay device id, copied from that
/// peer's own Settings page) to start a chat, and separately pastes back an "invite" blob to
/// accept one. No MAUI dependency in this partial (only Domain/Data interfaces +
/// <see cref="ContactCardCodec"/>) — the Clipboard/Shell-navigation commands that need MAUI live
/// in <c>NewChatViewModel.Actions.cs</c>, same split rationale as <see cref="DocumentBrowserViewModel"/>.
/// </summary>
public sealed partial class NewChatViewModel : ObservableObject
{
    private readonly IMessagingService _messagingService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITransportSettingsRepository _transportSettingsRepository;

    [ObservableProperty]
    public partial string PeerContactCardText { get; set; }

    [ObservableProperty]
    public partial string? GeneratedInviteText { get; set; }

    [ObservableProperty]
    public partial bool HasGeneratedInvite { get; set; }

    [ObservableProperty]
    public partial string InviteBlobText { get; set; }

    [ObservableProperty]
    public partial ChatSession? CreatedSession { get; set; }

    [ObservableProperty]
    public partial ChatSession? AcceptedSession { get; set; }

    [ObservableProperty]
    public partial bool HasAcceptedSession { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public NewChatViewModel(
        IMessagingService messagingService,
        ICurrentUserService currentUserService,
        ITransportSettingsRepository transportSettingsRepository)
    {
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));

        PeerContactCardText = string.Empty;
        InviteBlobText = string.Empty;
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    partial void OnGeneratedInviteTextChanged(string? value) => HasGeneratedInvite = !string.IsNullOrEmpty(value);

    partial void OnAcceptedSessionChanged(ChatSession? value) => HasAcceptedSession = value is not null;

    [RelayCommand]
    private async Task CreateSessionAsync()
    {
        StatusErrorMessage = null;
        GeneratedInviteText = null;
        CreatedSession = null;

        ContactCardBlob peerCard;
        try
        {
            peerCard = ContactCardCodec.Decode<ContactCardBlob>(PeerContactCardText);
        }
        catch (Exception)
        {
            StatusErrorMessage = "That doesn't look like a valid contact card — check you copied the whole block.";
            return;
        }

        IsBusy = true;
        try
        {
            var ownCard = await BuildOwnContactCardAsync();
            var (session, handshakeCipherText) = await _messagingService.CreateSessionAsync(peerCard.DisplayName, peerCard.PublicKey, peerCard.RelayDeviceId);

            var invite = new ChatInviteBlob(ownCard.DisplayName, ownCard.PublicKey, ownCard.RelayDeviceId, handshakeCipherText);
            GeneratedInviteText = ContactCardCodec.Encode(invite);
            CreatedSession = session;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not start the chat: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AcceptInviteAsync()
    {
        StatusErrorMessage = null;
        AcceptedSession = null;

        ChatInviteBlob invite;
        try
        {
            invite = ContactCardCodec.Decode<ChatInviteBlob>(InviteBlobText);
        }
        catch (Exception)
        {
            StatusErrorMessage = "That doesn't look like a valid invite — check you copied the whole block.";
            return;
        }

        IsBusy = true;
        try
        {
            AcceptedSession = await _messagingService.AcceptSessionAsync(
                invite.InitiatorDisplayName, invite.InitiatorPublicKey, invite.InitiatorRelayDeviceId, invite.HandshakeCipherText);
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not accept the invite: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Public (not private) so a console test can build a card the same way the "Start a new chat" flow does, without going through Clipboard.</summary>
    internal async Task<ContactCardBlob> BuildOwnContactCardAsync()
    {
        var configuration = await _transportSettingsRepository.GetAsync();
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Register with a relay in Settings first.");
        var publicKey = await _messagingService.GetLocalIdentityPublicKeyAsync();
        return new ContactCardBlob(_currentUserService.Current.DisplayName, publicKey, deviceId);
    }
}
