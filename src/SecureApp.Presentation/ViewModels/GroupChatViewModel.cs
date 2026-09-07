using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// One group chat's message thread + member list. See <see cref="GroupChat"/>'s own remarks for the
/// crypto design (a full mesh of ordinary pairwise <see cref="ChatSession"/>s, fanned out to on
/// every send — <em>not</em> a new group ratchet) — this view model is what actually drives that
/// fan-out and the corresponding "collapse my own N outbound copies back into one bubble" read side.
/// </summary>
public sealed partial class GroupChatViewModel : ObservableObject, IQueryAttributable
{
    private readonly IGroupChatRepository _groupChatRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IMessagingService _messagingService;
    private readonly IMessageTransport _messageTransport;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISharedLibraryService _libraryService;
    private readonly IContactDirectoryService _contactDirectoryService;

    private Guid _groupChatId;
    private byte[] _localPublicKey = [];
    private byte[] _founderPublicKey = [];
    private IReadOnlyList<GroupMember> _members = [];
    private EventHandler<MessageEnvelope>? _envelopeReceivedHandler;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<GroupMessageItem> Messages { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<GroupMemberItem> Members { get; set; }

    [ObservableProperty]
    public partial string ComposeText { get; set; }

    [ObservableProperty]
    public partial bool CanSend { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    /// <summary>Founder, or an Admin/Modifier-role device — see <c>RbacAction.InviteGroupMember</c>'s own remarks for why Modifier already counts as "an authorized user" in this app's 3-role model.</summary>
    [ObservableProperty]
    public partial bool CanManageMembers { get; set; }

    [ObservableProperty]
    public partial bool IsShowingAddMember { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SelectableMemberItem> AddableMembers { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingAddableMembers { get; set; }

    [ObservableProperty]
    public partial Guid? PendingAttachmentLibraryFileId { get; set; }

    [ObservableProperty]
    public partial string? PendingAttachmentFileName { get; set; }

    [ObservableProperty]
    public partial bool HasPendingAttachment { get; set; }

    public GroupChatViewModel(
        IGroupChatRepository groupChatRepository,
        IGroupMemberRepository groupMemberRepository,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        ICurrentUserService currentUserService,
        ISharedLibraryService libraryService,
        IContactDirectoryService contactDirectoryService)
    {
        _groupChatRepository = groupChatRepository ?? throw new ArgumentNullException(nameof(groupChatRepository));
        _groupMemberRepository = groupMemberRepository ?? throw new ArgumentNullException(nameof(groupMemberRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _contactDirectoryService = contactDirectoryService ?? throw new ArgumentNullException(nameof(contactDirectoryService));

        Title = "Skupina";
        Messages = [];
        Members = [];
        AddableMembers = [];
        ComposeText = string.Empty;
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    partial void OnComposeTextChanged(string value) => RecomputeCanSend();

    partial void OnPendingAttachmentFileNameChanged(string? value)
    {
        HasPendingAttachment = !string.IsNullOrEmpty(value);
        RecomputeCanSend();
    }

    private void RecomputeCanSend() => CanSend = !string.IsNullOrWhiteSpace(ComposeText) || HasPendingAttachment;

    public void SetPendingAttachment(Guid libraryFileId, string fileName)
    {
        PendingAttachmentLibraryFileId = libraryFileId;
        PendingAttachmentFileName = fileName;
    }

    [RelayCommand]
    private void ClearPendingAttachment()
    {
        PendingAttachmentLibraryFileId = null;
        PendingAttachmentFileName = null;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("groupChatId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _groupChatId = id;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            var group = await _groupChatRepository.GetByIdAsync(_groupChatId);
            if (group is null)
            {
                StatusErrorMessage = "Tuto skupinu se nepodařilo najít.";
                return;
            }

            Title = group.Name;
            _founderPublicKey = group.FounderPublicKey;
            _localPublicKey = await _messagingService.GetLocalIdentityPublicKeyAsync();

            await _currentUserService.InitializeAsync();
            var isFounder = _founderPublicKey.AsSpan().SequenceEqual(_localPublicKey);
            CanManageMembers = isFounder || RoleAccessPolicy.IsAllowed(_currentUserService.Current.Role, RbacAction.InviteGroupMember);

            _members = await _groupMemberRepository.GetByGroupAsync(_groupChatId);
            Members = new ObservableCollection<GroupMemberItem>(_members.Select(m => new GroupMemberItem(
                m.Id,
                m.DisplayName,
                m.PublicKey,
                IsMe: m.PublicKey.AsSpan().SequenceEqual(_localPublicKey),
                IsFounder: m.PublicKey.AsSpan().SequenceEqual(_founderPublicKey),
                ViewerCanManage: CanManageMembers,
                RemoveCommand,
                ResetPairingCommand)));

            await LoadMessagesAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se načíst skupinu: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadMessagesAsync()
    {
        var rawMessages = await _messageRepository.GetByGroupAsync(_groupChatId);

        // A sender's own outbound message exists as one Message row per OTHER member it fanned out
        // to (all sharing the same GroupMessageId) — collapse those back into a single bubble here,
        // keeping only the first. Inbound messages never need this: each arrives as exactly one row.
        var seenOutboundGroupMessageIds = new HashSet<Guid>();
        var items = new List<GroupMessageItem>();

        foreach (var message in rawMessages.OrderBy(m => m.CreatedAtUtc))
        {
            if (message.Direction == MessageDirection.Outbound)
            {
                if (message.GroupMessageId is { } groupMessageId && !seenOutboundGroupMessageIds.Add(groupMessageId))
                    continue; // already rendered this logical message from an earlier fan-out leg
            }

            var senderName = message.Direction == MessageDirection.Outbound
                ? _currentUserService.Current.DisplayName
                : await ResolveSenderDisplayNameAsync(message.ChatSessionId);

            var text = await TryDecryptAsync(message);
            items.Add(new GroupMessageItem(message.Id, message.Direction == MessageDirection.Outbound, senderName, text, message.CreatedAtUtc, message.AttachmentLibraryFileId, message.AttachmentFileName));
        }

        Messages = new ObservableCollection<GroupMessageItem>(items.OrderBy(m => m.SentAtUtc));
    }

    private async Task<string> ResolveSenderDisplayNameAsync(Guid chatSessionId)
    {
        var session = await _chatSessionRepository.GetByIdAsync(chatSessionId);
        return session?.PeerDisplayName ?? "Neznámý člen";
    }

    private async Task<string> TryDecryptAsync(Message message)
    {
        try
        {
            var plaintext = await _messagingService.DecryptMessageAsync(message.Id);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return "[Tuto zprávu se nepodařilo dešifrovat]";
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (!CanSend) return;

        var text = ComposeText;
        var attachmentId = PendingAttachmentLibraryFileId;
        var attachmentName = PendingAttachmentFileName;
        ComposeText = string.Empty;
        ClearPendingAttachmentCommand.Execute(null);
        StatusErrorMessage = null;

        var groupMessageId = Guid.NewGuid();
        var plaintext = Encoding.UTF8.GetBytes(text);
        var otherMembers = _members.Where(m => !m.PublicKey.AsSpan().SequenceEqual(_localPublicKey)).ToList();
        var unreachable = new List<string>();

        try
        {
            foreach (var member in otherMembers)
            {
                var session = await _messagingService.FindExistingSessionAsync(member.PublicKey);
                if (session is null)
                {
                    // Not yet paired with this member (their device hasn't come online to complete
                    // the pairwise handshake since being added) — skip them for this message rather
                    // than blocking the whole send; once paired they'll simply have missed messages
                    // sent before that point, same "no history backfill" gap 1:1 chat already has
                    // for an offline recipient beyond the relay's own outbox window.
                    unreachable.Add(member.DisplayName);
                    continue;
                }

                try
                {
                    var (_, envelope) = await _messagingService.SendMessageAsync(
                        session.Id, plaintext, attachmentLibraryFileId: attachmentId, attachmentFileName: attachmentName,
                        groupChatId: _groupChatId, groupMessageId: groupMessageId);

                    if (_messageTransport.IsConnected)
                    {
                        try { await _messageTransport.SendEnvelopeAsync(envelope); }
                        catch { /* stays Pending in storage, same policy ChatViewModel.SendAsync already uses */ }
                    }
                }
                catch
                {
                    // One member's send failing must never stop delivery to the rest of the group.
                    unreachable.Add(member.DisplayName);
                }
            }

            Messages.Add(new GroupMessageItem(Guid.NewGuid(), true, _currentUserService.Current.DisplayName, text, DateTimeOffset.UtcNow, attachmentId, attachmentName));

            if (unreachable.Count > 0)
                StatusErrorMessage = $"Nedoručeno: {string.Join(", ", unreachable)} (zatím nespárováno nebo offline).";
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se odeslat: {ex.Message}";
        }
    }

    /// <summary>
    /// Recovery for a specific member's pairwise session whose Double Ratchet state has become
    /// genuinely undecryptable (2026-09-07) — same underlying action as
    /// <c>ChatListViewModel.ResetSessionAsync</c>, just reachable directly from the group's own
    /// member list instead of requiring a detour through the 1:1 chat list, where every "Local
    /// User" row looks identical and there's no way to tell which one is this member. Only closes
    /// THIS device's side — see the button's own remarks in <c>GroupChatPage.xaml</c> for why the
    /// OTHER device needs to do the same before re-pairing actually completes.
    /// </summary>
    [RelayCommand]
    private async Task ResetPairingAsync(GroupMemberItem? member)
    {
        if (member is null || member.IsMe) return;

        StatusErrorMessage = null;
        try
        {
            var session = await _messagingService.FindExistingSessionAsync(member.PublicKey);
            if (session is null)
            {
                StatusErrorMessage = $"S uživatelem {member.DisplayName} zatím není žádné aktivní párování.";
                return;
            }

            await _messagingService.CloseSessionAsync(session.Id);
            StatusErrorMessage = $"Párování s {member.DisplayName} zrušeno na tomto zařízení. Stejné tlačítko musí použít i {member.DisplayName} na svém zařízení — pak se přes „+ Přidat“ nebo Nový chat spárujete znovu.";
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se zrušit párování: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenAttachmentAsync(GroupMessageItem? item)
    {
        if (item?.AttachmentLibraryFileId is not { } libraryFileId) return;

        StatusErrorMessage = null;
        try
        {
            var document = await _libraryService.DownloadAndImportAsync(libraryFileId);
            await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}");
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít '{item.AttachmentFileName}': {ex.Message}";
        }
    }

    // --- Membership management (founder/admin/authorized-user only, see CanManageMembers) ---

    [RelayCommand]
    private async Task ToggleAddMemberAsync()
    {
        IsShowingAddMember = !IsShowingAddMember;
        if (IsShowingAddMember)
            await LoadAddableMembersAsync();
    }

    [RelayCommand]
    private async Task RemoveAsync(Guid memberId)
    {
        if (!CanManageMembers) return;

        var remaining = _members.Where(m => m.Id != memberId).ToList();
        await BroadcastMembershipAsync(remaining);
    }

    /// <summary>
    /// Replaces the group's full membership snapshot (2026-09-07 — see <c>IGroupMemberRepository.ReplaceAllAsync</c>'s
    /// own remarks on why this is always a full list, never a diff), persists it locally, and
    /// re-broadcasts it to every member including any brand new one — the same
    /// pair-if-needed-then-send-invite logic <c>NewGroupViewModel.CreateGroupAsync</c> runs at
    /// creation time, just re-run here for an existing group.
    /// </summary>
    private async Task BroadcastMembershipAsync(IReadOnlyList<GroupMember> newMembers)
    {
        StatusErrorMessage = null;
        try
        {
            await _groupMemberRepository.ReplaceAllAsync(_groupChatId, newMembers);
            _members = newMembers;
            Members = new ObservableCollection<GroupMemberItem>(newMembers.Select(m => new GroupMemberItem(
                m.Id, m.DisplayName, m.PublicKey,
                IsMe: m.PublicKey.AsSpan().SequenceEqual(_localPublicKey),
                IsFounder: m.PublicKey.AsSpan().SequenceEqual(_founderPublicKey),
                ViewerCanManage: CanManageMembers,
                RemoveCommand,
                ResetPairingCommand)));

            var inviteBlob = ContactCardCodec.Encode(new GroupInviteBlob(
                _groupChatId, Title, _founderPublicKey,
                newMembers.Select(m => new GroupMemberBlob(m.DisplayName, m.PublicKey, m.RelayDeviceId)).ToList()));

            foreach (var member in newMembers)
            {
                if (member.PublicKey.AsSpan().SequenceEqual(_localPublicKey))
                    continue;

                try
                {
                    if (await _messagingService.FindExistingSessionAsync(member.PublicKey) is null)
                        await _messagingService.CreateSessionAsync(member.DisplayName, member.PublicKey, member.RelayDeviceId);

                    if (_messageTransport.IsConnected)
                        await _messageTransport.SendGroupInviteAsync(member.RelayDeviceId, inviteBlob);
                }
                catch
                {
                    // Best-effort per member, same reasoning as NewGroupViewModel.CreateGroupAsync.
                }
            }

            await LoadMessagesAsync(); // a removed member's future messages just stop arriving; past ones stay visible
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se aktualizovat členy skupiny: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadAddableMembersAsync()
    {
        IsLoadingAddableMembers = true;
        try
        {
            var directoryMembers = await _contactDirectoryService.ListMembersAsync();
            var alreadyIn = _members.Select(m => Convert.ToBase64String(m.PublicKey)).ToHashSet();
            AddableMembers = new ObservableCollection<SelectableMemberItem>(
                directoryMembers
                    .Where(m => !alreadyIn.Contains(Convert.ToBase64String(m.PublicKey)))
                    .Select(m => new SelectableMemberItem(m.RelayDeviceId, m.DisplayName, m.PublicKey)));
        }
        catch
        {
            // Best-effort — see NewChatViewModel.LoadMembersAsync's identical remark.
        }
        finally
        {
            IsLoadingAddableMembers = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAddMembersAsync()
    {
        var toAdd = AddableMembers.Where(m => m.IsSelected).ToList();
        if (toAdd.Count == 0) return;

        var newMembers = _members.Concat(toAdd.Select(m => new GroupMember(_groupChatId, m.DisplayName, m.PublicKey, m.RelayDeviceId))).ToList();
        await BroadcastMembershipAsync(newMembers);
        IsShowingAddMember = false;
    }

    /// <summary>Call from the page's OnAppearing (paired with <see cref="StopListening"/> in OnDisappearing) — mirrors <c>ChatViewModel.StartListening</c> exactly, just filtering by <c>GroupChatId</c> instead of a single <c>SessionId</c>.</summary>
    public void StartListening()
    {
        if (_envelopeReceivedHandler is not null) return;

        _envelopeReceivedHandler = (_, envelope) =>
            MainThread.BeginInvokeOnMainThread(() => _ = HandleEnvelopeReceivedAsync(envelope));
        _messageTransport.EnvelopeReceived += _envelopeReceivedHandler;
    }

    public void StopListening()
    {
        if (_envelopeReceivedHandler is null) return;
        _messageTransport.EnvelopeReceived -= _envelopeReceivedHandler;
        _envelopeReceivedHandler = null;
    }

    private async Task HandleEnvelopeReceivedAsync(MessageEnvelope envelope)
    {
        if (envelope.GroupChatId != _groupChatId) return;

        try
        {
            var message = await _messagingService.ReceiveMessageAsync(envelope);
            var senderName = await ResolveSenderDisplayNameAsync(message.ChatSessionId);
            var text = await TryDecryptAsync(message);
            Messages.Add(new GroupMessageItem(message.Id, false, senderName, text, message.CreatedAtUtc, message.AttachmentLibraryFileId, message.AttachmentFileName));
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se zpracovat příchozí zprávu skupiny: {ex.Message}";
        }
    }
}

/// <summary>One message in a group's thread — unlike <see cref="ChatMessageItem"/>, always carries the sender's display name, since "who sent this" isn't implicit the way it is in a 1:1 thread. <see cref="IsInbound"/> is a plain negation kept as its own field (not a XAML converter) — this codebase's established pattern (see <c>SettingsViewModel.HasPendingActivations</c>'s own remarks) so no binding ever needs to negate another.</summary>
public sealed record GroupMessageItem(Guid Id, bool IsOutbound, string SenderDisplayName, string Text, DateTimeOffset SentAtUtc, Guid? AttachmentLibraryFileId = null, string? AttachmentFileName = null)
{
    public bool HasAttachment => AttachmentLibraryFileId is not null;
    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool IsInbound => !IsOutbound;
}

/// <summary>
/// One row in a group's member list — carries the shared <see cref="RemoveCommand"/>/<see cref="ResetPairingCommand"/>
/// instances (bound per-item as <c>CommandParameter="{Binding .}"</c>/<c>Id</c>), same pattern this
/// codebase already uses elsewhere for per-item actions. <see cref="DisplayNameWithRoleSuffix"/> and
/// <see cref="CanRemove"/> are pre-computed here rather than in XAML, same no-converters
/// convention as <see cref="GroupMessageItem.IsInbound"/>. <see cref="PublicKey"/> (2026-09-07) is
/// what <c>GroupChatViewModel.ResetPairingAsync</c> actually needs to look up this member's
/// pairwise <c>ChatSession</c> — not otherwise displayed.
/// </summary>
public sealed record GroupMemberItem(Guid Id, string DisplayName, byte[] PublicKey, bool IsMe, bool IsFounder, bool ViewerCanManage, System.Windows.Input.ICommand RemoveCommand, System.Windows.Input.ICommand ResetPairingCommand)
{
    public string DisplayNameWithRoleSuffix => (IsMe, IsFounder) switch
    {
        (true, true) => $"{DisplayName} (já, zakladatel)",
        (true, false) => $"{DisplayName} (já)",
        (false, true) => $"{DisplayName} (zakladatel)",
        _ => DisplayName
    };

    /// <summary>Unlike <see cref="CanRemove"/>, resetting a broken pairing doesn't need <see cref="ViewerCanManage"/> — it only touches this device's own copy of a session it's already a party to, not the group's shared membership list, so there's no reason to gate it behind the same admin/founder permission.</summary>
    public bool CanResetPairing => !IsMe;

    /// <summary>Requires BOTH that the person looking at this list is allowed to manage members at all (<see cref="ViewerCanManage"/>, mirroring <c>GroupChatViewModel.CanManageMembers</c> at the time this row was built) AND that this particular row isn't the viewer themselves or the founder — leaving/founder-transfer isn't built in this pass (see DEVELOPMENT_PLAN.md's remarks).</summary>
    public bool CanRemove => ViewerCanManage && !IsMe && !IsFounder;
}
