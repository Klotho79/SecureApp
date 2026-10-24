using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Chat;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Pairs a new chat session. The one path (2026-09-16, user's ask "1:1 by mel byt jako skupinovy"): pick
/// someone from the relay's member directory (<see cref="Members"/>) and tap once —
/// <see cref="CreateSessionWithPeerAsync"/> does the rest, and the peer's app completes the pairing on its
/// own (<c>App.OnPairingInviteReceived</c>). The manual contact-card / invite paste and QR-scan fallback was
/// removed from the UI that day and its dead view-model half was deleted on 2026-10-24. No MAUI dependency
/// in this partial (only Domain/Data interfaces + <see cref="ContactCardCodec"/>) — the Shell-navigation
/// command that needs MAUI lives in <c>NewChatViewModel.Actions.cs</c>, same split rationale as
/// <see cref="DocumentBrowserViewModel"/>.
/// </summary>
public sealed partial class NewChatViewModel : ObservableObject
{
    private readonly IMessagingService _messagingService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly IMessageTransport _messageTransport;
    private readonly IContactDirectoryService _contactDirectoryService;
    private readonly IDiagnosticsReporter _diagnosticsReporter;
    private readonly ISharedLibraryService _sharedLibraryService;

    /// <summary>Every other community member the relay already knows about — see the class-level remarks. Refreshed on page appear (<see cref="LoadMembersAsync"/>), not live-updated; a member who activates while this page is open just needs a pull-to-refresh-equivalent re-open, same freshness tradeoff <c>LibraryViewModel.Categories</c> already accepts.</summary>
    [ObservableProperty]
    public partial ObservableCollection<DirectoryMemberItem> Members { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMembers { get; set; }

    [ObservableProperty]
    public partial bool HasNoMembers { get; set; }

    [ObservableProperty]
    public partial ChatSession? CreatedSession { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Neutral (not error) status — used for "you're already paired with this contact" rather than a genuine failure.</summary>
    [ObservableProperty]
    public partial string? StatusInfoMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusInfo { get; set; }

    /// <summary>Drives "Open Chat"'s visibility independent of whether an invite was actually generated — set when re-pairing with an already-known peer just opens the existing session instead (see CreateSessionWithPeerAsync's own remarks).</summary>
    [ObservableProperty]
    public partial bool HasCreatedSession { get; set; }

    public NewChatViewModel(
        IMessagingService messagingService,
        ICurrentUserService currentUserService,
        ITransportSettingsRepository transportSettingsRepository,
        IMessageTransport messageTransport,
        IContactDirectoryService contactDirectoryService,
        IDiagnosticsReporter diagnosticsReporter,
        ISharedLibraryService sharedLibraryService)
    {
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _contactDirectoryService = contactDirectoryService ?? throw new ArgumentNullException(nameof(contactDirectoryService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));

        Members = [];
        HasNoMembers = true;
    }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Warning, value!, nameof(NewChatViewModel));
    }

    partial void OnStatusInfoMessageChanged(string? value) => HasStatusInfo = !string.IsNullOrEmpty(value);

    partial void OnCreatedSessionChanged(ChatSession? value) => HasCreatedSession = value is not null;

    /// <summary>Best-effort reconnect before listing, same reasoning as <c>ChatViewModel.EnsureConnectedAsync</c> — a dropped connection shouldn't have to wait for this page's next open to notice.</summary>
    private Task EnsureConnectedAsync() => Transport.RelayConnection.EnsureConnectedAsync(_messageTransport, _transportSettingsRepository);

    /// <summary>
    /// Fetches the relay's member directory — see the class-level remarks. A failure here (relay
    /// unreachable, not yet connected) leaves the list empty; it stays a quiet
    /// <see cref="StatusInfoMessage"/> rather than a red <see cref="StatusErrorMessage"/> but is not silent
    /// (2026-09-07, a real complaint elsewhere in this app about an unexplained empty list applies here
    /// too): an empty directory list says why, instead of just looking like nothing happened.
    /// </summary>
    [RelayCommand]
    private async Task LoadMembersAsync()
    {
        IsLoadingMembers = true;
        await EnsureConnectedAsync();
        try
        {
            var members = await _contactDirectoryService.ListMembersAsync();
            Members = new ObservableCollection<DirectoryMemberItem>(
                members.Select(m => new DirectoryMemberItem(m.RelayDeviceId, m.DisplayName, m.PublicKey, StartChatWithMemberCommand)));
            HasNoMembers = Members.Count == 0;
            if (HasNoMembers) StatusInfoMessage = null;
        }
        catch (Exception ex)
        {
            HasNoMembers = Members.Count == 0;
            StatusInfoMessage = $"Seznam členů komunity se teď nepodařilo načíst ({ex.Message}) — zkuste to za chvíli znovu.";
        }
        finally
        {
            IsLoadingMembers = false;
        }
    }

    /// <summary>One-tap path (2026-09-06): builds the same <see cref="ContactCardBlob"/> a pasted card would have carried, directly from a directory entry.</summary>
    [RelayCommand]
    private Task StartChatWithMemberAsync(DirectoryMemberItem? member)
    {
        if (member is null)
            return Task.CompletedTask;

        var peerCard = new ContactCardBlob(member.DisplayName, member.PublicKey, member.RelayDeviceId);
        return CreateSessionWithPeerAsync(peerCard);
    }

    /// <summary>
    /// Checks IMessagingService.FindExistingSessionAsync before minting a fresh session — a real gap the
    /// user caught live (2026-09-05): starting a chat with the same peer twice used to silently create a
    /// second, indistinguishable session every time, since CreateSessionAsync itself always mints
    /// unconditionally (kept that way — see its own remarks and IMessagingService.FindExistingSessionAsync's).
    /// Already-paired just opens the existing chat instead of repeating a handshake that would go nowhere
    /// anyway (the peer's UI has no reason to re-accept an invite from someone it already has an active
    /// ratchet with).
    /// </summary>
    private async Task CreateSessionWithPeerAsync(ContactCardBlob peerCard)
    {
        StatusErrorMessage = null;
        StatusInfoMessage = null;
        CreatedSession = null;

        IsBusy = true;
        try
        {
            var existing = await _messagingService.FindExistingSessionAsync(peerCard.PublicKey);
            if (existing is not null)
            {
                // Nothing to show or share — jump straight in. See OpenCreatedChatAsync's own
                // remarks (2026-09-06) for why this auto-navigates instead of waiting for a second
                // "Open Chat" tap the user rightly called out as pointless extra friction.
                CreatedSession = existing;
                // Already paired but might not have the shared library key yet (e.g. this device
                // generated/imported one after pairing originally happened) — see
                // SharedLibraryKeySync's own remarks. Fire-and-forget: best-effort by design, never
                // blocks jumping into the chat.
                _ = SharedLibraryKeySync.OfferKeyAsync(_sharedLibraryService, _messagingService, _messageTransport, existing.Id, _diagnosticsReporter);
                await OpenCreatedChatCommand.ExecuteAsync(null);
                return;
            }

            // Starting a chat with this peer is an explicit action — clear any earlier user-removal so
            // the chat isn't treated as suppressed (2.2, 2026-09-14).
            RemovedPeersStore.Remove(peerCard.PublicKey);

            var ownCard = await BuildOwnContactCardAsync();
            var (session, handshakeCipherText) = await _messagingService.CreateSessionAsync(peerCard.DisplayName, peerCard.PublicKey, peerCard.RelayDeviceId);

            var invite = new ChatInviteBlob(ownCard.DisplayName, ownCard.PublicKey, ownCard.RelayDeviceId, handshakeCipherText);
            var inviteBlob = ContactCardCodec.Encode(invite);
            CreatedSession = session;

            // 2026-09-06 pairing simplification (user's own request: "co nejméně zatěžující pro
            // uživatele" — as little burden on the user as possible): the invite goes over the relay
            // directly — App.xaml.cs's OnPairingInviteReceived completes pairing on their end
            // automatically, with zero action needed there either.
            var delivered = false;
            if (_messageTransport.IsConnected)
            {
                try
                {
                    await _messageTransport.SendPairingInviteAsync(peerCard.RelayDeviceId, inviteBlob);
                    delivered = true;
                }
                catch
                {
                    // Falls through to the retry below — never surfaced as an error, same "never let
                    // live-send failure block the user" policy ChatViewModel.SendAsync already
                    // established for ordinary messages.
                }
            }

            // 2026-10-24: there is no QR/copy UI to hand a not-sent invite to any more, so a send that did
            // not happen (relay down, or the send threw) used to leave this device with a pending session
            // and a peer that never heard of it until the one-hour pending-handshake sweep. The supervisor
            // re-sends whatever is stored here the moment the relay is connected (~seconds).
            if (!delivered)
                PendingInviteStore.Set(peerCard.PublicKey, session.Id, peerCard.RelayDeviceId, inviteBlob);

            // Best-effort shared-library-key offer (2026-09-10) — see SharedLibraryKeySync's own remarks.
            _ = SharedLibraryKeySync.OfferKeyAsync(_sharedLibraryService, _messagingService, _messageTransport, session.Id, _diagnosticsReporter);

            // Jump straight in either way — if the peer could not complete their end yet, the chat is
            // reachable from the list and the pairing finishes by itself once the invite gets through.
            StatusInfoMessage = delivered
                ? $"Odesláno automaticky — spárování s {peerCard.DisplayName} by mělo proběhnout za chvíli."
                : $"Spárování s {peerCard.DisplayName} se dokončí automaticky, jakmile bude relay dostupný.";
            await OpenCreatedChatCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se zahájit chat: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Builds this device's own contact card (name, identity public key, relay device id) for the invite.</summary>
    internal async Task<ContactCardBlob> BuildOwnContactCardAsync()
    {
        var configuration = await _transportSettingsRepository.GetAsync();
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var publicKey = await _messagingService.GetLocalIdentityPublicKeyAsync();
        await _currentUserService.InitializeAsync(); // defensive — see HttpContactDirectoryService.PublishSelfAsync's own remarks on why this must never be skipped
        return new ContactCardBlob(_currentUserService.Current.DisplayName, publicKey, deviceId);
    }
}

/// <summary>One row in the "New Chat" member-directory list — carries the same shared <c>StartChatWithMemberCommand</c> instance (bound per-item as <c>CommandParameter="{Binding .}"</c> in the DataTemplate) rather than an <c>x:Reference</c> back to the page, same pattern this codebase already uses for admin/category rows elsewhere.</summary>
public sealed record DirectoryMemberItem(Guid RelayDeviceId, string DisplayName, byte[] PublicKey, ICommand Command);
