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
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly IDiagnosticsReporter _diagnosticsReporter;

    private Guid _groupChatId;
    private byte[] _localPublicKey = [];
    private byte[] _founderPublicKey = [];
    private IReadOnlyList<GroupMember> _members = [];
    private IReadOnlyDictionary<string, string> _directoryNames = new Dictionary<string, string>();
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

    /// <summary>Collapsed by default (2026-09-09 — real complaint: the member card "zabírá třetinu displeje", a third of the screen) so the actual conversation gets the space on a phone; tap the header to expand.</summary>
    [ObservableProperty]
    public partial bool IsMembersExpanded { get; set; }

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
        IContactDirectoryService contactDirectoryService,
        ITransportSettingsRepository transportSettingsRepository,
        IDiagnosticsReporter diagnosticsReporter)
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
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));

        Title = "Skupina";
        Messages = [];
        Members = [];
        AddableMembers = [];
        ComposeText = string.Empty;
    }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Error, value!, nameof(GroupChatViewModel));
    }

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
            // 2026-09-09: always resolve against the relay's CURRENT directory rather than trusting
            // whatever name got captured once at invite time — see DirectoryNameResolver's own
            // remarks. Stored on the instance so ResolveSenderDisplayNameAsync (message list) uses
            // the same fresh snapshot without a second directory fetch.
            _directoryNames = await DirectoryNameResolver.BuildAsync(_contactDirectoryService);
            Members = new ObservableCollection<GroupMemberItem>(_members.Select(m => new GroupMemberItem(
                m.Id,
                DirectoryNameResolver.Resolve(_directoryNames, m.PublicKey, m.DisplayName),
                m.PublicKey,
                IsMe: m.PublicKey.AsSpan().SequenceEqual(_localPublicKey),
                IsFounder: m.PublicKey.AsSpan().SequenceEqual(_founderPublicKey),
                ViewerCanManage: CanManageMembers,
                RemoveCommand,
                ResetPairingCommand)));

            await LoadMessagesAsync();

            // Auto-heal on open (2026-09-07) — the user's explicit demand after several rounds of
            // this needing a manual nudge: opening the group screen is now the ONLY thing needed to
            // fix every broken/missing pairwise link with the other members, with no button anywhere
            // in this path. Fire-and-forget so it never blocks the screen from showing.
            _ = ResyncMissingMembersAsync();
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
        if (session is null) return "Neznámý člen";
        return DirectoryNameResolver.Resolve(_directoryNames, session.PeerIdentityPublicKey, session.PeerDisplayName);
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
        // Each entry names WHO and WHY (2026-09-07 — "prosím doplň řádně chybová hlášení... aby
        // bylo jasné kde nastala chyba"), not just a bare name — a per-member reason is what
        // actually lets anyone tell "not paired yet" apart from "the send itself failed" apart from
        // "not connected to the relay" without having to go dig through logs.
        var undelivered = new List<(string Name, string Reason)>();

        try
        {
            foreach (var member in otherMembers)
            {
                var session = await _messagingService.FindExistingSessionAsync(member.PublicKey);
                if (session is null)
                {
                    // Not yet paired with this member (their device hasn't come online to complete
                    // the pairwise handshake since being added — or a previous session with them
                    // broke and got closed) — skip them for THIS message rather than blocking the
                    // whole send, but kick off a resync in the background (2026-09-07) so a future
                    // message has a real chance: matches this app's "the app should reconnect on
                    // its own" recovery policy rather than requiring a manual reset tap first.
                    undelivered.Add((member.DisplayName, "zatím nespárováno — appka se pokusí spárovat na pozadí"));
                    _ = TryBackgroundResyncAsync(member);
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
                        catch (Exception sendEx) { undelivered.Add((member.DisplayName, $"uloženo, ale nepodařilo se odeslat na relay: {sendEx.Message}")); /* stays Pending in storage, same policy ChatViewModel.SendAsync already uses */ }
                    }
                    else
                    {
                        undelivered.Add((member.DisplayName, "appka teď není připojená k relay — zůstává čekající, odešle se po obnovení spojení"));
                    }
                }
                catch (Exception encryptEx)
                {
                    // One member's send failing must never stop delivery to the rest of the group.
                    undelivered.Add((member.DisplayName, $"šifrování/odeslání selhalo: {encryptEx.Message}"));
                }
            }

            Messages.Add(new GroupMessageItem(Guid.NewGuid(), true, _currentUserService.Current.DisplayName, text, DateTimeOffset.UtcNow, attachmentId, attachmentName));

            if (undelivered.Count > 0)
                StatusErrorMessage = $"Nedoručeno {undelivered.Count} z {otherMembers.Count}: " + string.Join("; ", undelivered.Select(u => $"{u.Name} ({u.Reason})"));
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
    /// User" row looks identical and there's no way to tell which one is this member.
    ///
    /// A single tap fully resolves it — <see cref="SessionRecoveryHelper.ResyncAsync"/> closes,
    /// re-handshakes and re-invites all in one call, and the OTHER device auto-accepts the fresh
    /// invite (<c>App.OnPairingInviteReceived</c>) without needing to press anything itself. An
    /// earlier version required the same button pressed on both devices with no actual re-pairing
    /// step after that — a dead end caught live; see this method's own git history.
    /// </summary>
    [RelayCommand]
    private async Task ResetPairingAsync(GroupMemberItem? member)
    {
        if (member is null || member.IsMe) return;

        var groupMember = _members.FirstOrDefault(m => m.PublicKey.AsSpan().SequenceEqual(member.PublicKey));
        if (groupMember is null) return;

        StatusErrorMessage = null;
        try
        {
            await SessionRecoveryHelper.ResyncAsync(
                _messagingService, _messageTransport, _transportSettingsRepository, _currentUserService,
                groupMember.DisplayName, groupMember.PublicKey, groupMember.RelayDeviceId);
            StatusErrorMessage = $"Spojení s {member.DisplayName} bylo obnoveno.";
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se obnovit spojení s {member.DisplayName}: {ex.Message}";
        }
    }

    /// <summary>
    /// Auto-heal on open (2026-09-07): checks every OTHER member for a currently active pairwise
    /// session and resyncs any that don't have one — covers both a member who was never paired
    /// (their device came online only after the original group invite went out — a real gap the
    /// user's 3-way live test kept hitting: with 3 devices dropping in and out all session, a pair
    /// missing its handshake entirely looked identical to a broken one from the outside) and one
    /// whose session broke and got Closed but never actually got re-established since. Runs every
    /// time this screen loads — opening the group is now the whole fix, nothing else to press.
    /// </summary>
    private async Task ResyncMissingMembersAsync()
    {
        var others = _members.Where(m => !m.PublicKey.AsSpan().SequenceEqual(_localPublicKey)).ToList();
        foreach (var member in others)
        {
            ChatSession? existing;
            try { existing = await _messagingService.FindExistingSessionAsync(member.PublicKey); }
            catch { continue; }

            if (existing is not null)
                continue; // already paired and healthy — nothing to do

            await TryBackgroundResyncAsync(member);
        }
    }

    /// <summary>Fire-and-forget opportunistic resync for a member <see cref="SendAsync"/> (or <see cref="ResyncMissingMembersAsync"/>) just found unpaired — best-effort by design, same reasoning as this file's other best-effort per-member catches; a failure here only means the NEXT attempt is no better off than this one, never a crash.</summary>
    private async Task TryBackgroundResyncAsync(GroupMember member)
    {
        try
        {
            await SessionRecoveryHelper.ResyncAsync(
                _messagingService, _messageTransport, _transportSettingsRepository, _currentUserService,
                member.DisplayName, member.PublicKey, member.RelayDeviceId);
        }
        catch
        {
            // Best-effort — see the remark above.
        }
    }

    /// <summary>
    /// Auto-heal (2026-09-07): triggered from <see cref="HandleEnvelopeReceivedAsync"/>'s catch
    /// block whenever a genuine ratchet decrypt failure surfaces (not the idempotency guard's
    /// silent-duplicate case — that never throws) — resyncs the broken member's pairwise session
    /// automatically, no button press required on either device. The one message that failed to
    /// decrypt is unrecoverable either way (Double Ratchet forward secrecy — see
    /// <c>MessagingService.ReceiveMessageAsync</c>'s own remarks), but everything the member sends
    /// after this point should go through cleanly once the resync completes.
    /// </summary>
    private async Task TryAutoHealAsync(Guid sessionId, Exception originalError)
    {
        try
        {
            var session = await _chatSessionRepository.GetByIdAsync(sessionId);
            var member = session is null
                ? null
                : _members.FirstOrDefault(m => m.PublicKey.AsSpan().SequenceEqual(session.PeerIdentityPublicKey));

            if (session is null || member is null)
            {
                StatusErrorMessage = $"Nepodařilo se zpracovat příchozí zprávu skupiny: {originalError.Message}";
                return;
            }

            await SessionRecoveryHelper.ResyncAsync(
                _messagingService, _messageTransport, _transportSettingsRepository, _currentUserService,
                member.DisplayName, member.PublicKey, member.RelayDeviceId);
            StatusErrorMessage = $"Spojení s {member.DisplayName} se automaticky obnovilo na pozadí. Tahle jedna zpráva se ztratila, další už by měly projít v pořádku.";
        }
        catch (Exception healEx)
        {
            StatusErrorMessage = $"Zprávu se nepodařilo zpracovat a automatické obnovení spojení selhalo: {healEx.Message}";
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

    [RelayCommand]
    private void ToggleMembersExpanded() => IsMembersExpanded = !IsMembersExpanded;

    // --- Membership management (founder/admin/authorized-user only, see CanManageMembers) ---

    [RelayCommand]
    private async Task ToggleAddMemberAsync()
    {
        IsShowingAddMember = !IsShowingAddMember;
        if (IsShowingAddMember)
        {
            IsMembersExpanded = true; // "+ Přidat" only makes sense expanded — see IsMembersExpanded's own remarks
            await LoadAddableMembersAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(Guid memberId)
    {
        if (!CanManageMembers) return;

        var remaining = _members.Where(m => m.Id != memberId).ToList();
        await BroadcastMembershipAsync(remaining);
    }

    /// <summary>
    /// Voluntary self-removal (2026-09-10, user's own ask: "přidej možnost vystoupení z chatu") —
    /// deliberately NOT gated behind <see cref="CanManageMembers"/>, unlike <see cref="RemoveAsync"/>
    /// above: removing yourself needs no "authorized user" permission, only removing someone ELSE
    /// does. Reuses the exact same <see cref="BroadcastMembershipAsync"/> every other membership
    /// change already goes through, so every other member's device picks up the new (self-excluded)
    /// snapshot the same way it would for any other membership change — no separate "someone left"
    /// message type needed. Confirmation dialog lives in <c>GroupChatPage</c>'s code-behind, this
    /// codebase's established convention; this method runs once the user has confirmed.
    ///
    /// Known gap, not fixed here (matches this app's already-documented "a founder can't hand off
    /// the role" note): if the FOUNDER leaves, <c>GroupChat.FounderPublicKey</c> keeps pointing at a
    /// public key no longer in the member list — deliberately out of scope for this pass, same as
    /// every other founder-succession question already deferred.
    /// </summary>
    [RelayCommand]
    private async Task LeaveGroupAsync()
    {
        var remaining = _members.Where(m => !m.PublicKey.AsSpan().SequenceEqual(_localPublicKey)).ToList();
        await BroadcastMembershipAsync(remaining);

        try
        {
            // Removes this device's own local copy too — after telling everyone else, staying
            // around locally would just mean the group reappears the next time ANY membership
            // change is rebroadcast by someone who doesn't yet know this device already left.
            await _groupChatRepository.DeleteAsync(_groupChatId);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Skupina byla opuštěna, ale lokální záznam se nepodařilo smazat: {ex.Message}";
        }
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
            _directoryNames = await DirectoryNameResolver.BuildAsync(_contactDirectoryService);
            Members = new ObservableCollection<GroupMemberItem>(newMembers.Select(m => new GroupMemberItem(
                m.Id, DirectoryNameResolver.Resolve(_directoryNames, m.PublicKey, m.DisplayName), m.PublicKey,
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
                    var memberSession = await _messagingService.FindExistingSessionAsync(member.PublicKey);
                    if (memberSession is null)
                        (memberSession, _) = await _messagingService.CreateSessionAsync(member.DisplayName, member.PublicKey, member.RelayDeviceId);

                    if (_messageTransport.IsConnected)
                        await _messageTransport.SendGroupInviteAsync(member.RelayDeviceId, inviteBlob);

                    // Best-effort shared-library-key offer (2026-09-10) — see SharedLibraryKeySync's
                    // own remarks; every group member's pairwise session gets the same offer a 1:1
                    // pairing already would.
                    await SharedLibraryKeySync.OfferKeyAsync(_libraryService, _messagingService, _messageTransport, memberSession.Id);
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

    /// <summary>Best-effort reconnect before listing addable members, same reasoning as <c>ChatViewModel.EnsureConnectedAsync</c>.</summary>
    private async Task EnsureConnectedAsync()
    {
        if (_messageTransport.IsConnected) return;
        try
        {
            var configuration = await _transportSettingsRepository.GetAsync();
            if (configuration is { AssignedDeviceId: not null, IsAutoConnectEnabled: true, EndpointUri: { } endpoint })
                await _messageTransport.ConnectAsync(endpoint);
        }
        catch
        {
            // Best-effort — LoadAddableMembersAsync below surfaces whatever's still wrong.
        }
    }

    /// <summary>
    /// Unlike <c>NewChatViewModel.LoadMembersAsync</c>, there's no manual paste/QR fallback for
    /// adding an existing group's member — this IS the only path — so a failure here (2026-09-07,
    /// same "an unexplained empty list looks exactly like a bug" complaint as <c>NewGroupViewModel</c>)
    /// gets a real <see cref="StatusErrorMessage"/>, not silence.
    /// </summary>
    [RelayCommand]
    private async Task LoadAddableMembersAsync()
    {
        IsLoadingAddableMembers = true;
        StatusErrorMessage = null;
        await EnsureConnectedAsync();
        try
        {
            var directoryMembers = await _contactDirectoryService.ListMembersAsync();
            var alreadyIn = _members.Select(m => Convert.ToBase64String(m.PublicKey)).ToHashSet();
            AddableMembers = new ObservableCollection<SelectableMemberItem>(
                directoryMembers
                    .Where(m => !alreadyIn.Contains(Convert.ToBase64String(m.PublicKey)))
                    .Select(m => new SelectableMemberItem(m.RelayDeviceId, m.DisplayName, m.PublicKey)));
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se načíst seznam členů komunity: {ex.Message}";
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
            await TryAutoHealAsync(envelope.SessionId, ex);
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
