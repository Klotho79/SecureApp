using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Lists existing chat sessions. Split like <see cref="DocumentBrowserViewModel"/>: this partial
/// is free of any MAUI-<em>framework</em> type (only Domain interfaces + CommunityToolkit.Mvvm), so
/// it's directly testable from a plain console app with mocked interfaces; <c>ChatListViewModel.Actions.cs</c>
/// holds the two Shell-navigation commands. <see cref="IMessagingService"/>/<see cref="IMessageTransport"/>/etc.
/// (2026-09-07, for <see cref="ResetSessionAsync"/>'s auto-resync) are just more Domain interfaces,
/// same as the two repositories already here — they don't break that claim.
/// </summary>
public sealed partial class ChatListViewModel : ObservableObject
{
    private readonly IChatSessionRepository _sessionRepository;
    private readonly IGroupChatRepository _groupChatRepository;
    private readonly IMessagingService _messagingService;
    private readonly IMessageTransport _messageTransport;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IContactDirectoryService _contactDirectoryService;

    [ObservableProperty]
    public partial ObservableCollection<ChatSessionItem> Sessions { get; set; }

    /// <summary>Group chats (2026-09-07) — listed separately from 1:1 <see cref="Sessions"/> rather than merged into one collection, so each keeps its own simple, already-tested item template (a group needs no peer-relay-device concept the way a 1:1 row does, and a 1:1 row needs no member count).</summary>
    [ObservableProperty]
    public partial ObservableCollection<GroupChatListItem> Groups { get; set; }

    [ObservableProperty]
    public partial bool HasNoGroups { get; set; }

    [ObservableProperty]
    public partial bool HasGroups { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Drives ONLY the pull-to-refresh spinner (2026-09-11). Kept separate from the automatic
    /// on-appear reload so returning to the list never flashes the refresh circle — that reload is
    /// quiet (local names instantly, network refresh in the background).</summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    private IReadOnlyDictionary<string, string> _displayedNames = new Dictionary<string, string>();

    public ChatListViewModel(
        IChatSessionRepository sessionRepository,
        IGroupChatRepository groupChatRepository,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        ITransportSettingsRepository transportSettingsRepository,
        ICurrentUserService currentUserService,
        IContactDirectoryService contactDirectoryService)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _groupChatRepository = groupChatRepository ?? throw new ArgumentNullException(nameof(groupChatRepository));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _contactDirectoryService = contactDirectoryService ?? throw new ArgumentNullException(nameof(contactDirectoryService));
        Sessions = [];
        Groups = [];
        HasNoGroups = true;
    }

    partial void OnHasNoGroupsChanged(bool value) => HasGroups = !value;

    /// <summary>
    /// Quiet reload used on appear (2026-09-11) — builds the lists from the LAST-KNOWN directory
    /// names with no network call and no spinner, so returning to the list is instant and never
    /// flashes the pull-to-refresh circle. A fresh name fetch runs in the background and rebuilds
    /// only if names actually changed. The user-initiated pull-to-refresh is <see cref="RefreshAsync"/>.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await BuildListsAsync(DirectoryNameResolver.LastKnown);
        _ = RefreshNamesInBackgroundAsync();
    }

    /// <summary>Pull-to-refresh (user-initiated) — this one DOES show the refresh spinner while it fetches fresh names from the relay.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var names = await DirectoryNameResolver.BuildAsync(_contactDirectoryService);
            await BuildListsAsync(names.Count > 0 ? names : DirectoryNameResolver.LastKnown);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task BuildListsAsync(IReadOnlyDictionary<string, string> directoryNames)
    {
        _displayedNames = directoryNames;

        // One chat per PEER, not per session (2026-09-13, the user's live bug: "sami od sebe se tvoří
        // nové a nové chaty"). Resync/auto-heal/group-mesh CLOSE the old session and CREATE a new one
        // each time (see SessionRecoveryHelper.ResyncAsync), leaving stale Closed rows behind — listing
        // every session showed each of those as a separate chat. Collapse by peer identity key, keeping
        // the one that best represents the current chat (prefer a non-Closed session, then the most
        // recent). Nothing is deleted here, so no message history is lost — only the duplicates are
        // hidden from the list.
        var sessions = await _sessionRepository.GetAllAsync();
        var oneSessionPerPeer = sessions
            .GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
            .Select(peer => peer
                .OrderByDescending(s => s.State != ChatSessionState.Closed) // prefer a live session over a Closed one
                .ThenByDescending(s => s.LastRatchetedAtUtc ?? s.CreatedAtUtc) // then the most recent
                .First());

        Sessions = new ObservableCollection<ChatSessionItem>(
            oneSessionPerPeer.OrderByDescending(s => s.LastRatchetedAtUtc ?? s.CreatedAtUtc)
                .Select(s =>
                {
                    var name = DirectoryNameResolver.Resolve(directoryNames, s.PeerIdentityPublicKey, s.PeerDisplayName);
                    return new ChatSessionItem(s.Id, name, s.State, DescribeLastActivity(s), ComputeInitials(name));
                }));
        IsEmpty = Sessions.Count == 0;

        var groups = await _groupChatRepository.GetAllAsync();
        Groups = new ObservableCollection<GroupChatListItem>(
            groups.OrderByDescending(g => g.ModifiedAtUtc)
                .Select(g => new GroupChatListItem(g.Id, g.Name, ComputeInitials(g.Name))));
        HasNoGroups = Groups.Count == 0;
    }

    /// <summary>Fetches current names from the relay AFTER the list is already shown, and rebuilds only if they differ — same no-flicker pattern as the chat threads.</summary>
    private async Task RefreshNamesInBackgroundAsync()
    {
        try
        {
            var names = await DirectoryNameResolver.BuildAsync(_contactDirectoryService);
            if (names.Count == 0) return;
            if (DirectoryNameResolver.AreEquivalent(names, _displayedNames)) return;
            await BuildListsAsync(names);
        }
        catch
        {
            // Best-effort — the list already shows local names.
        }
    }

    [ObservableProperty]
    public partial string? ResetErrorMessage { get; set; }

    /// <summary>
    /// Fully resyncs a 1:1 chat session in one tap (2026-09-07) — the recovery path for a session
    /// whose Double Ratchet state has become genuinely unrecoverable (not a duplicate-delivery false
    /// alarm, which <c>MessagingService.ReceiveMessageAsync</c>'s own idempotency guard already
    /// handles silently — this is for when decryption itself throws a real error).
    ///
    /// An earlier version of this just closed the session and told the user to press the same
    /// button on the OTHER device too — that turned out to be a dead end live (nothing ever
    /// re-paired afterward). <see cref="SessionRecoveryHelper.ResyncAsync"/> now does the whole
    /// close-plus-fresh-handshake-plus-invite sequence right here, and the receiving side
    /// (<c>App.OnPairingInviteReceived</c>) always accepts a fresh invite from an already-paired
    /// peer instead of silently ignoring it — so only THIS device needs to act.
    /// Confirmation dialog lives in <c>ChatListPage</c>'s code-behind, this codebase's established
    /// "native prompts live in the page" convention — this method runs once the user has confirmed.
    /// </summary>
    [RelayCommand]
    private async Task ResetSessionAsync(ChatSessionItem? item)
    {
        if (item is null) return;

        ResetErrorMessage = null;
        var session = await _sessionRepository.GetByIdAsync(item.Id);
        if (session is null) return;

        if (session.PeerRelayDeviceId is not { } relayDeviceId)
        {
            ResetErrorMessage = $"S {session.PeerDisplayName} chybí propojení na relay zařízení — obnovit spojení nelze.";
            return;
        }

        try
        {
            await SessionRecoveryHelper.ResyncAsync(
                _messagingService, _messageTransport, _transportSettingsRepository, _currentUserService,
                session.PeerDisplayName, session.PeerIdentityPublicKey, relayDeviceId);
        }
        catch (Exception ex)
        {
            ResetErrorMessage = $"Nepodařilo se obnovit spojení s {session.PeerDisplayName}: {ex.Message}";
        }

        await LoadAsync();
    }

    [ObservableProperty]
    public partial string? DeleteErrorMessage { get; set; }

    /// <summary>
    /// Deletes a 1:1 chat from THIS device's own list only (2026-09-10, user's own ask: "mazání
    /// chatu asi jen ze seznamu toho uživatele, který si to přeje, jinak bude pozvaný") —
    /// deliberately local, not a two-sided "unpair" the peer's device is told about. Cascades the
    /// session's own messages via the schema's own <c>ON DELETE CASCADE</c> (see
    /// <c>SqlCipherConnectionFactory</c>) — <see cref="IChatSessionRepository.DeleteAsync"/> already
    /// existed for exactly this, just never wired to any UI until now. The user's own explicit,
    /// accepted tradeoff: since the peer's own session state is untouched, a later message from
    /// them (or this device's own "unknown sender" auto-pair / resync sweeps) can silently recreate
    /// a fresh session — deleting doesn't guarantee the peer stays gone, only that today's clutter
    /// is gone right now. Confirmation dialog lives in <c>ChatListPage</c>'s code-behind.
    /// </summary>
    [RelayCommand]
    private async Task DeleteSessionAsync(ChatSessionItem? item)
    {
        if (item is null) return;
        DeleteErrorMessage = null;
        try
        {
            await _sessionRepository.DeleteAsync(item.Id);
            AppLog.Event("chat.deleted", ("peer", item.PeerDisplayName), ("session", item.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            DeleteErrorMessage = $"Nepodařilo se smazat chat s {item.PeerDisplayName}: {ex.Message}";
            AppLog.Error("ChatList.DeleteSession", "delete chat failed", ex);
        }
    }

    /// <summary>
    /// Same local-only deletion as <see cref="DeleteSessionAsync"/>, for a group instead — removes
    /// this device's own <c>GroupChat</c> row (cascading its <c>GroupMember</c> rows via the schema's
    /// own <c>ON DELETE CASCADE</c>) without notifying anyone else, unlike <see cref="GroupChatViewModel.LeaveGroupCommand"/>
    /// (a real, broadcast "I'm out" that removes this device from the shared membership snapshot).
    /// Deleting here does NOT do that — this device is still a member per every other participant's
    /// own copy, so the group can reappear the next time someone else rebroadcasts a membership
    /// change (the same "jinak bude pozvaný" tradeoff the user explicitly accepted). Choosing between
    /// this and "Opustit" is deliberate: delete for clutter cleanup, leave for actually exiting.
    /// </summary>
    [RelayCommand]
    private async Task DeleteGroupAsync(GroupChatListItem? item)
    {
        if (item is null) return;
        DeleteErrorMessage = null;
        try
        {
            await _groupChatRepository.DeleteAsync(item.Id);
            AppLog.Event("group.deleted-local", ("group", item.Id), ("name", item.Name));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            DeleteErrorMessage = $"Nepodařilo se smazat skupinu {item.Name}: {ex.Message}";
            AppLog.Error("ChatList.DeleteGroup", "delete group failed", ex);
        }
    }

    private static string DescribeLastActivity(ChatSession session) => session.State switch
    {
        ChatSessionState.Closed => "Uzavřeno",
        ChatSessionState.PendingHandshake => "Čeká na připojení…",
        _ => session.LastRatchetedAtUtc is { } last ? last.LocalDateTime.ToString("g") : "Zatím žádné zprávy"
    };

    /// <summary>Up to 2 letters for the avatar circle in the redesigned list (2026-09-06) — first letter of up to the first two words, e.g. "Dr. B. Chen" -&gt; "DB". Plain string, not a MAUI Color, to keep this partial's own stated MAUI-free claim true; the avatar's actual color is one fixed accent tint set in XAML, not per-contact.</summary>
    private static string ComputeInitials(string displayName)
    {
        var letters = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]))
            .Take(2)
            .ToArray();
        return letters.Length == 0 ? "?" : new string(letters);
    }
}

public sealed record ChatSessionItem(Guid Id, string PeerDisplayName, ChatSessionState State, string LastActivityText, string Initials);

public sealed record GroupChatListItem(Guid Id, string Name, string Initials);
