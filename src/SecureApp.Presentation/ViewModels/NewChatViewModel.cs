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

    // --- QR pairing (2026-09-05) — an alternative to copy/paste for the same two blobs above,
    // nothing protocol-related changes: a successful scan just fills the same Text property a
    // manual paste would have. ---

    [ObservableProperty]
    public partial bool IsScanningPeerCard { get; set; }

    [ObservableProperty]
    public partial bool IsScanningInvite { get; set; }

    [ObservableProperty]
    public partial bool IsShowingInviteQr { get; set; }

    /// <summary>The same invite, packed via QrBlobCodec for the "Show QR" flow — see that class's own remarks for why the invite (it carries a full handshake ciphertext) needs this rather than GeneratedInviteText's copy/paste format.</summary>
    [ObservableProperty]
    public partial string? GeneratedInviteQrValue { get; set; }

    [ObservableProperty]
    public partial string ScanPeerCardButtonText { get; set; }

    [ObservableProperty]
    public partial string ScanInviteButtonText { get; set; }

    /// <summary>Neutral (not error) status — used for "you're already paired with this contact" rather than a genuine failure. See CreateSessionAsync/AcceptInviteAsync's own remarks.</summary>
    [ObservableProperty]
    public partial string? StatusInfoMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusInfo { get; set; }

    /// <summary>Drives "Open Chat"'s visibility independent of whether an invite was actually generated — set when re-pairing with an already-known peer just opens the existing session instead (see CreateSessionAsync's own remarks).</summary>
    [ObservableProperty]
    public partial bool HasCreatedSession { get; set; }

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
        ScanPeerCardButtonText = "Scan QR";
        ScanInviteButtonText = "Scan QR";
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    partial void OnStatusInfoMessageChanged(string? value) => HasStatusInfo = !string.IsNullOrEmpty(value);

    partial void OnGeneratedInviteTextChanged(string? value) => HasGeneratedInvite = !string.IsNullOrEmpty(value);

    partial void OnCreatedSessionChanged(ChatSession? value) => HasCreatedSession = value is not null;

    partial void OnAcceptedSessionChanged(ChatSession? value) => HasAcceptedSession = value is not null;

    partial void OnIsScanningPeerCardChanged(bool value) => ScanPeerCardButtonText = value ? "Cancel Scan" : "Scan QR";

    partial void OnIsScanningInviteChanged(bool value) => ScanInviteButtonText = value ? "Cancel Scan" : "Scan QR";

    /// <summary>
    /// Checks IMessagingService.FindExistingSessionAsync before minting a fresh session — a real
    /// gap the user caught live (2026-09-05): pasting/scanning the same peer's contact card twice
    /// used to silently create a second, indistinguishable session every time, since
    /// CreateSessionAsync itself always mints unconditionally (kept that way — see its own remarks
    /// and IMessagingService.FindExistingSessionAsync's). Already-paired just opens the existing
    /// chat instead of repeating a handshake that would go nowhere anyway (the peer's UI has no
    /// reason to re-accept an invite from someone it already has an active ratchet with).
    /// </summary>
    [RelayCommand]
    private async Task CreateSessionAsync()
    {
        StatusErrorMessage = null;
        StatusInfoMessage = null;
        GeneratedInviteText = null;
        GeneratedInviteQrValue = null;
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
            var existing = await _messagingService.FindExistingSessionAsync(peerCard.PublicKey);
            if (existing is not null)
            {
                CreatedSession = existing;
                StatusInfoMessage = $"You're already paired with {peerCard.DisplayName} — opening the existing chat.";
                return;
            }

            var ownCard = await BuildOwnContactCardAsync();
            var (session, handshakeCipherText) = await _messagingService.CreateSessionAsync(peerCard.DisplayName, peerCard.PublicKey, peerCard.RelayDeviceId);

            var invite = new ChatInviteBlob(ownCard.DisplayName, ownCard.PublicKey, ownCard.RelayDeviceId, handshakeCipherText);
            GeneratedInviteText = ContactCardCodec.Encode(invite);
            GeneratedInviteQrValue = QrBlobCodec.EncodeInvite(invite);
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

    /// <summary>Same already-paired check as CreateSessionAsync — see its own remarks.</summary>
    [RelayCommand]
    private async Task AcceptInviteAsync()
    {
        StatusErrorMessage = null;
        StatusInfoMessage = null;
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
            var existing = await _messagingService.FindExistingSessionAsync(invite.InitiatorPublicKey);
            if (existing is not null)
            {
                AcceptedSession = existing;
                StatusInfoMessage = $"You're already paired with {invite.InitiatorDisplayName} — opening the existing chat.";
                return;
            }

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

    [RelayCommand]
    private void ToggleScanPeerCard() => IsScanningPeerCard = !IsScanningPeerCard;

    [RelayCommand]
    private void ToggleScanInvite() => IsScanningInvite = !IsScanningInvite;

    [RelayCommand]
    private void ToggleShowInviteQr() => IsShowingInviteQr = !IsShowingInviteQr;

    /// <summary>
    /// Called from NewChatPage's code-behind when the peer-card scanner (CameraBarcodeReaderView)
    /// detects a QR code. The scanned value is QrBlobCodec's compact format (see its own remarks),
    /// not ContactCardCodec's copy/paste format — decoded then immediately re-encoded through
    /// ContactCardCodec so CreateSessionCommand's existing paste-handling logic never needs to
    /// know a QR was involved at all.
    /// </summary>
    public void OnPeerCardScanned(string qrValue)
    {
        IsScanningPeerCard = false;
        try
        {
            var card = QrBlobCodec.DecodeContactCard(qrValue);
            PeerContactCardText = ContactCardCodec.Encode(card);
        }
        catch (Exception)
        {
            StatusErrorMessage = "That QR code doesn't look like a valid contact card.";
        }
    }

    /// <summary>Called from NewChatPage's code-behind when the invite scanner detects a QR code — see OnPeerCardScanned's remarks, same decode-then-re-encode reasoning.</summary>
    public void OnInviteScanned(string qrValue)
    {
        IsScanningInvite = false;
        try
        {
            var invite = QrBlobCodec.DecodeInvite(qrValue);
            InviteBlobText = ContactCardCodec.Encode(invite);
        }
        catch (Exception)
        {
            StatusErrorMessage = "That QR code doesn't look like a valid invite.";
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
