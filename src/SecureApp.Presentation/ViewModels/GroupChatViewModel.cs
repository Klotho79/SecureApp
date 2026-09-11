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

    // On-demand history pagination + a per-session sender-name map, mirroring ChatViewModel
    // (2026-09-11). _olderLogicalRows holds the deduped older logical messages (undecrypted), oldest
    // first, revealed a page at a time on scroll-up. _sessionNameById avoids the old per-message DB
    // query that resolved each sender's name — built once per load instead.
    private readonly List<Message> _olderLogicalRows = [];
    private Dictionary<Guid, string> _sessionNameById = [];
    private bool _isLoadingOlder;
    private const int InitialMessageCount = 12;
    private const int OlderPageSize = 20;

    /// <summary>See ChatViewModel.AnimationSettleMs — hold the UI assignment back until the open/back animation has settled, while the load runs in parallel on a background thread.</summary>
    private const int AnimationSettleMs = 280;

    /// <summary>Everything the group thread needs, computed entirely on a background thread so it can run in parallel with the open animation without touching the UI (2026-09-11).</summary>
    private sealed record GroupInitialLoad(
        GroupChat Group,
        byte[] LocalPublicKey,
        Role Role,
        IReadOnlyList<GroupMember> Members,
        IReadOnlyDictionary<string, string> DirectoryNames,
        Dictionary<Guid, string> SessionNames,
        List<Message> OlderLogical,
        List<GroupMessageItem> Items,
        string Signature);

    /// <summary>Raised after older history is prepended, carrying the previously-top item so the page can re-anchor (no jump) — mirrors ChatViewModel.ScrollAnchorRequested.</summary>
    public event Action<GroupMessageItem>? ScrollAnchorRequested;

    /// <summary>True while older group history remains unrevealed.</summary>
    public bool HasOlderMessages => _olderLogicalRows.Count > 0;

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

    // Warm message-thread cache (2026-09-11) — see ChatViewModel's own remarks. Caches the group's
    // decrypted message list so a revisit shows it instantly (populated before the slide); the member
    // list + per-session state still load in LoadAsync but the expensive per-message decrypt is skipped
    // when the cheap signature says nothing changed.
    private sealed record CachedGroupThread(string Title, List<GroupMessageItem> Items, List<Message> Older, string Signature);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CachedGroupThread> _groupThreadCache = new();

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("groupChatId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _groupChatId = id;

        // Show the cached thread synchronously, before the page slides in (content already in place).
        if (_groupThreadCache.TryGetValue(_groupChatId, out var cached))
        {
            Title = cached.Title;
            _olderLogicalRows.Clear();
            _olderLogicalRows.AddRange(cached.Older);
            Messages = new ObservableCollection<GroupMessageItem>(cached.Items);
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true; // the spinner itself only appears if this lasts — see DelayedActivityIndicator
        StatusErrorMessage = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Phase 1 — load EVERYTHING (group, members, sessions, messages + decrypt) on a
            // background thread, in parallel with the open animation, never touching the UI. Uses
            // the last-known directory names (no network); the relay refresh happens later in the
            // background. Only the newest InitialMessageCount are decrypted.
            var loaded = await Task.Run<GroupInitialLoad?>(async () =>
            {
                var group = await _groupChatRepository.GetByIdAsync(_groupChatId);
                if (group is null) return null;

                var localPublicKey = await _messagingService.GetLocalIdentityPublicKeyAsync();
                await _currentUserService.InitializeAsync();
                var role = _currentUserService.Current.Role;
                var ownName = _currentUserService.Current.DisplayName;
                var members = await _groupMemberRepository.GetByGroupAsync(_groupChatId);

                var directoryNames = DirectoryNameResolver.LastKnown;
                var allSessions = await _chatSessionRepository.GetAllAsync();
                var sessionNames = allSessions.ToDictionary(
                    s => s.Id,
                    s => DirectoryNameResolver.Resolve(directoryNames, s.PeerIdentityPublicKey, s.PeerDisplayName));

                // If the already-shown cached copy is still current, skip the expensive per-message
                // decrypt entirely — reuse the cached items. Member/session state above is cheap and
                // always loaded (needed for the member list, sending, delete gating).
                var signature = await _messageRepository.GetGroupSignatureAsync(_groupChatId);
                if (_groupThreadCache.TryGetValue(_groupChatId, out var c) && c.Signature == signature)
                    return new GroupInitialLoad(group, localPublicKey, role, members, directoryNames, sessionNames, c.Older, c.Items, signature);

                var rawMessages = await _messageRepository.GetByGroupAsync(_groupChatId);
                var seen = new HashSet<Guid>();
                var logical = new List<Message>();
                foreach (var m in rawMessages.OrderBy(x => x.CreatedAtUtc))
                {
                    if (m.IsSystemPayload) continue;
                    if (m.Direction == MessageDirection.Outbound && m.GroupMessageId is { } gid && !seen.Add(gid)) continue;
                    logical.Add(m);
                }

                var initialStart = Math.Max(0, logical.Count - InitialMessageCount);
                var older = logical.Take(initialStart).ToList();
                var initialRows = logical.Skip(initialStart).ToList();

                var items = new List<GroupMessageItem>(initialRows.Count);
                foreach (var m in initialRows)
                {
                    var isOwn = m.Direction == MessageDirection.Outbound;
                    var senderName = isOwn ? ownName : (sessionNames.TryGetValue(m.ChatSessionId, out var n) ? n : "Neznámý člen");
                    var text = await TryDecryptAsync(m);
                    var canDelete = RoleAccessPolicy.CanDeleteMessage(role, isOwn, m.SenderRole);
                    items.Add(new GroupMessageItem(m.Id, isOwn, senderName, text, m.CreatedAtUtc, m.AttachmentLibraryFileId, m.AttachmentFileName, canDelete, m.GroupMessageId));
                }

                return new GroupInitialLoad(group, localPublicKey, role, members, directoryNames, sessionNames, older, items, signature);
            });

            if (loaded is null)
            {
                StatusErrorMessage = "Tuto skupinu se nepodařilo najít.";
                return;
            }

            // Phase 2 — wait out the rest of the animation (usually already elapsed), THEN touch the UI.
            var remaining = AnimationSettleMs - (int)sw.ElapsedMilliseconds;
            if (remaining > 0) await Task.Delay(remaining);

            _founderPublicKey = loaded.Group.FounderPublicKey;
            _localPublicKey = loaded.LocalPublicKey;
            var isFounder = _founderPublicKey.AsSpan().SequenceEqual(_localPublicKey);
            CanManageMembers = isFounder || RoleAccessPolicy.IsAllowed(loaded.Role, RbacAction.InviteGroupMember);
            Title = loaded.Group.Name;
            _members = loaded.Members;
            _directoryNames = loaded.DirectoryNames;
            _sessionNameById = loaded.SessionNames;
            _olderLogicalRows.Clear();
            _olderLogicalRows.AddRange(loaded.OlderLogical);
            RebuildMemberList();
            Messages = new ObservableCollection<GroupMessageItem>(loaded.Items);
            _groupThreadCache[_groupChatId] = new CachedGroupThread(Title, loaded.Items, [.. loaded.OlderLogical], loaded.Signature);
            IsLoading = false;
            ScrollToBottomRequested?.Invoke();

            // Refresh names from the directory, then auto-heal broken pairings — both in the
            // background so neither blocks the thread. Auto-heal on open (2026-09-07) is the user's
            // explicit demand: opening the group screen is the only thing needed to fix broken links.
            _ = RefreshNamesInBackgroundAsync();
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

    /// <summary>Raised once the thread is (re)populated so the page can scroll to the newest message — see GroupChatPage's own subscription. Mirrors ChatViewModel.ScrollToBottomRequested.</summary>
    public event Action? ScrollToBottomRequested;

    /// <summary>Refreshes the warm cache with the currently-displayed group thread (2026-09-11) — called after a live send/receive/delete so the next open shows the up-to-date thread instantly. Best-effort.</summary>
    private async Task UpdateCacheAsync()
    {
        try
        {
            var signature = await _messageRepository.GetGroupSignatureAsync(_groupChatId);
            _groupThreadCache[_groupChatId] = new CachedGroupThread(Title, Messages.ToList(), [.. _olderLogicalRows], signature);
        }
        catch { /* best-effort — self-corrects on the next open via the signature check */ }
    }

    /// <summary>Projects <see cref="_members"/> into the bound <see cref="Members"/> chips using the current <see cref="_directoryNames"/> snapshot — factored out so the background refresh can rebuild them with fresh names without duplicating the projection.</summary>
    private void RebuildMemberList()
    {
        Members = new ObservableCollection<GroupMemberItem>(_members.Select(m => new GroupMemberItem(
            m.Id,
            DirectoryNameResolver.Resolve(_directoryNames, m.PublicKey, m.DisplayName),
            m.PublicKey,
            IsMe: m.PublicKey.AsSpan().SequenceEqual(_localPublicKey),
            IsFounder: m.PublicKey.AsSpan().SequenceEqual(_founderPublicKey),
            ViewerCanManage: CanManageMembers,
            RemoveCommand,
            ResetPairingCommand)));
    }

    /// <summary>
    /// Fetches the relay's current member directory AFTER the thread is already visible (2026-09-11
    /// perf), then rebuilds the member chips and reloads the messages so sender names reflect any
    /// rename. Best-effort: a failed/slow directory fetch just leaves the locally-cached names in
    /// place — DirectoryNameResolver.BuildAsync already returns empty on failure.
    /// </summary>
    private async Task RefreshNamesInBackgroundAsync()
    {
        try
        {
            var names = await DirectoryNameResolver.BuildAsync(_contactDirectoryService);
            if (names.Count == 0) return; // fetch failed — keep what we already showed
            if (DirectoryNameResolver.AreEquivalent(names, _directoryNames))
                return; // same names we already rendered with — no rebuild, no flicker

            _directoryNames = names;
            RebuildMemberList();
            await LoadMessagesAsync();
        }
        catch
        {
            // Best-effort — the names already on screen stay as they are.
        }
    }

    private async Task LoadMessagesAsync()
    {
        // Build the sender-name map ONCE from all sessions, instead of a DB query per message (the
        // old ResolveSenderDisplayNameAsync did GetByIdAsync for every single message — dozens of
        // round trips on the UI thread, part of the real cost of opening a busy group). 2026-09-11.
        var allSessions = await _chatSessionRepository.GetAllAsync();
        _sessionNameById = allSessions.ToDictionary(
            s => s.Id,
            s => DirectoryNameResolver.Resolve(_directoryNames, s.PeerIdentityPublicKey, s.PeerDisplayName));

        var rawMessages = await _messageRepository.GetByGroupAsync(_groupChatId);

        // Collapse each logical message down to one row (a sender's own message fans out as one row
        // per other member, all sharing GroupMessageId) and drop system payloads — all without
        // decrypting anything yet.
        var seenOutboundGroupMessageIds = new HashSet<Guid>();
        var logical = new List<Message>();
        foreach (var message in rawMessages.OrderBy(m => m.CreatedAtUtc))
        {
            if (message.IsSystemPayload) continue;
            if (message.Direction == MessageDirection.Outbound
                && message.GroupMessageId is { } gid && !seenOutboundGroupMessageIds.Add(gid))
                continue; // already have this logical message from an earlier fan-out leg
            logical.Add(message);
        }

        var initialStart = Math.Max(0, logical.Count - InitialMessageCount);
        var initialRows = logical.Skip(initialStart).ToList();
        _olderLogicalRows.Clear();
        _olderLogicalRows.AddRange(logical.Take(initialStart));

        var currentRole = _currentUserService.Current.Role;
        var initialItems = await Task.Run(async () =>
        {
            var list = new List<GroupMessageItem>(initialRows.Count);
            foreach (var message in initialRows)
                list.Add(await BuildItemAsync(message, currentRole));
            return list;
        });

        Messages = new ObservableCollection<GroupMessageItem>(initialItems);
        ScrollToBottomRequested?.Invoke();
        _ = UpdateCacheAsync();
    }

    /// <summary>Builds one group thread item from an already-loaded row (decrypt without a re-fetch + name from the preloaded map + delete-gating) — shared by initial load, older-page load, and live receive.</summary>
    private async Task<GroupMessageItem> BuildItemAsync(Message message, Role currentRole)
    {
        var isOwn = message.Direction == MessageDirection.Outbound;
        var senderName = isOwn
            ? _currentUserService.Current.DisplayName
            : (_sessionNameById.TryGetValue(message.ChatSessionId, out var name) ? name : "Neznámý člen");
        var text = await TryDecryptAsync(message);
        var canDelete = RoleAccessPolicy.CanDeleteMessage(currentRole, isOwn, message.SenderRole);
        return new GroupMessageItem(message.Id, isOwn, senderName, text, message.CreatedAtUtc, message.AttachmentLibraryFileId, message.AttachmentFileName, canDelete, message.GroupMessageId);
    }

    /// <summary>Reveals the next older page of group history on scroll-up (2026-09-11) — mirrors ChatViewModel.LoadOlderAsync, including the re-anchor so the view never jumps.</summary>
    [RelayCommand]
    private async Task LoadOlderAsync()
    {
        if (_isLoadingOlder || _olderLogicalRows.Count == 0) return;
        _isLoadingOlder = true;
        try
        {
            var pageStart = Math.Max(0, _olderLogicalRows.Count - OlderPageSize);
            var pageRows = _olderLogicalRows.Skip(pageStart).ToList();
            _olderLogicalRows.RemoveRange(pageStart, _olderLogicalRows.Count - pageStart);

            var currentRole = _currentUserService.Current.Role;
            var pageItems = await Task.Run(async () =>
            {
                var list = new List<GroupMessageItem>(pageRows.Count);
                foreach (var message in pageRows)
                    list.Add(await BuildItemAsync(message, currentRole));
                return list;
            });

            var anchor = Messages.Count > 0 ? Messages[0] : null;
            for (var i = pageItems.Count - 1; i >= 0; i--)
                Messages.Insert(0, pageItems[i]);

            if (anchor is not null)
                ScrollAnchorRequested?.Invoke(anchor);
        }
        catch
        {
            // Best-effort — the thread stays usable; next scroll-up retries.
        }
        finally
        {
            _isLoadingOlder = false;
        }
    }

    private async Task<string> TryDecryptAsync(Message message)
    {
        try
        {
            var plaintext = await _messagingService.DecryptMessageAsync(message); // already-loaded payload, no re-fetch
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

            // Own message — always deletable by this user; correlated across the fan-out by groupMessageId.
            Messages.Add(new GroupMessageItem(Guid.NewGuid(), true, _currentUserService.Current.DisplayName, text, DateTimeOffset.UtcNow, attachmentId, attachmentName, CanDelete: true, CorrelationId: groupMessageId));
            ScrollToBottomRequested?.Invoke();
            _ = UpdateCacheAsync();

            if (undelivered.Count > 0)
                StatusErrorMessage = $"Nedoručeno {undelivered.Count} z {otherMembers.Count}: " + string.Join("; ", undelivered.Select(u => $"{u.Name} ({u.Reason})"));
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se odeslat: {ex.Message}";
        }
    }

    /// <summary>
    /// Deletes a group message for everyone (2026-09-11) — RBAC-gated per
    /// <c>RoleAccessPolicy.CanDeleteMessage</c> (Admin any, Modifier own + Viewers', Viewer own only).
    /// Removes this device's local copies (all fan-out legs, matched by the shared group-message id)
    /// and fans a delete command out to every other member's pairwise session, tagged with this group
    /// id so each member's open thread updates live (and the app-wide handler removes it when closed).
    /// </summary>
    [RelayCommand]
    private async Task DeleteMessageAsync(GroupMessageItem? item)
    {
        if (item is null || item.CorrelationId is not { } correlationId) return;

        // Re-check RBAC (the item's CanDelete is a snapshot). Own messages are always deletable;
        // for someone else's, look up the stored sender role (inbound items carry a real message id).
        var actorRole = _currentUserService.Current.Role;
        bool allowed;
        if (item.IsOutbound)
        {
            allowed = true;
        }
        else
        {
            var stored = await _messageRepository.GetByIdAsync(item.Id);
            allowed = RoleAccessPolicy.CanDeleteMessage(actorRole, false, stored?.SenderRole);
        }

        if (!allowed)
        {
            StatusErrorMessage = "Na smazání této zprávy nemáte oprávnění.";
            return;
        }

        var confirmed = await Shell.Current.DisplayAlert("Smazat zprávu", "Opravdu smazat tuto zprávu? Smaže se u všech členů skupiny.", "Smazat", "Zrušit");
        if (!confirmed) return;

        try
        {
            await _messageRepository.DeleteByCorrelationAsync(correlationId);
            var doomed = Messages.Where(m => m.CorrelationId == correlationId).ToList();
            foreach (var doomedItem in doomed)
                Messages.Remove(doomedItem);
            _ = UpdateCacheAsync();

            var payload = MessageDeletionSync.BuildDeleteCommand(correlationId);
            var otherMembers = _members.Where(m => !m.PublicKey.AsSpan().SequenceEqual(_localPublicKey)).ToList();
            foreach (var member in otherMembers)
            {
                try
                {
                    var session = await _messagingService.FindExistingSessionAsync(member.PublicKey);
                    if (session is null) continue; // not paired right now — the app-wide handler on their side still applies it if/when they later receive... nothing to send here

                    var (_, envelope) = await _messagingService.SendMessageAsync(session.Id, payload, isSystemPayload: true, groupChatId: _groupChatId);
                    if (_messageTransport.IsConnected)
                        await _messageTransport.SendEnvelopeAsync(envelope);
                }
                catch
                {
                    // Best-effort per member, same policy as SendAsync's own fan-out.
                }
            }
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Zprávu se nepodařilo smazat: {ex.Message}";
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
            {
                // Already paired and healthy — nothing to resync, but still worth a best-effort
                // shared-library-key offer (2026-09-10, own cooldown) for the same reason
                // ChatViewModel.LoadAsync's own call does: a session paired before this mechanism
                // existed never gets a fresh pairing event to hang the offer off of.
                _ = SharedLibraryKeySync.OfferKeyAsync(_libraryService, _messagingService, _messageTransport, existing.Id, _diagnosticsReporter);
                continue;
            }

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
                    await SharedLibraryKeySync.OfferKeyAsync(_libraryService, _messagingService, _messageTransport, memberSession.Id, _diagnosticsReporter);
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

        // A delete command (2026-09-11) fans out tagged with this group id so it routes here; it's a
        // system payload, so it never shows as a bubble — handle it and stop. Ordinary messages fall
        // through to the visible-add path below.
        if (envelope.IsSystemPayload)
        {
            await HandleSystemPayloadAsync(envelope);
            return;
        }

        try
        {
            var message = await _messagingService.ReceiveMessageAsync(envelope);
            // Resolve the sender name from the preloaded map; if this is a brand-new session not yet
            // in it, fall back to a one-off lookup so a just-added member still shows a name.
            string senderName;
            if (_sessionNameById.TryGetValue(message.ChatSessionId, out var mapped))
            {
                senderName = mapped;
            }
            else
            {
                var session = await _chatSessionRepository.GetByIdAsync(message.ChatSessionId);
                senderName = session is null ? "Neznámý člen" : DirectoryNameResolver.Resolve(_directoryNames, session.PeerIdentityPublicKey, session.PeerDisplayName);
                if (session is not null) _sessionNameById[message.ChatSessionId] = senderName;
            }

            var text = await TryDecryptAsync(message);
            var canDelete = RoleAccessPolicy.CanDeleteMessage(_currentUserService.Current.Role, false, message.SenderRole);
            Messages.Add(new GroupMessageItem(message.Id, false, senderName, text, message.CreatedAtUtc, message.AttachmentLibraryFileId, message.AttachmentFileName, canDelete, message.GroupMessageId));
            ScrollToBottomRequested?.Invoke();
            _ = UpdateCacheAsync();
        }
        catch (Exception ex)
        {
            await TryAutoHealAsync(envelope.SessionId, ex);
        }
    }

    /// <summary>Handles a group-tagged system payload live while this thread is open — currently just a delete command (removes the target from the visible list; the DB delete is idempotent and also done by the app-wide handler).</summary>
    private async Task HandleSystemPayloadAsync(MessageEnvelope envelope)
    {
        try
        {
            var message = await _messagingService.ReceiveMessageAsync(envelope); // idempotent
            var plaintext = await _messagingService.DecryptMessageAsync(message.Id);
            if (MessageDeletionSync.TryParseDeleteCommand(plaintext, out var correlationId))
            {
                await _messageRepository.DeleteByCorrelationAsync(correlationId);
                var doomed = Messages.Where(m => m.CorrelationId == correlationId).ToList();
                foreach (var item in doomed)
                    Messages.Remove(item);
                if (doomed.Count > 0) _ = UpdateCacheAsync();
            }
        }
        catch
        {
            // Best-effort — a malformed/foreign system payload never disrupts the open thread.
        }
    }
}

/// <summary>One message in a group's thread — unlike <see cref="ChatMessageItem"/>, always carries the sender's display name, since "who sent this" isn't implicit the way it is in a 1:1 thread. <see cref="IsInbound"/> is a plain negation kept as its own field (not a XAML converter) — this codebase's established pattern (see <c>SettingsViewModel.HasPendingActivations</c>'s own remarks) so no binding ever needs to negate another.</summary>
public sealed record GroupMessageItem(Guid Id, bool IsOutbound, string SenderDisplayName, string Text, DateTimeOffset SentAtUtc, Guid? AttachmentLibraryFileId = null, string? AttachmentFileName = null, bool CanDelete = false, Guid? CorrelationId = null)
{
    public bool HasAttachment => AttachmentLibraryFileId is not null;
    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool IsInbound => !IsOutbound;

    /// <summary>Time for a message sent within the last day, otherwise the date — see ChatMessageItem.TimeLabel's own remarks.</summary>
    public string TimeLabel => (DateTimeOffset.Now - SentAtUtc).TotalHours >= 24
        ? SentAtUtc.LocalDateTime.ToString("d")
        : SentAtUtc.LocalDateTime.ToString("t");
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
