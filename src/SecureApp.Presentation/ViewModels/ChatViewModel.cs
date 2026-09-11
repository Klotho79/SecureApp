using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// One chat session's message thread. A single file — no <c>.Actions.cs</c> split needed: its MAUI
/// touches are <c>MainThread.BeginInvokeOnMainThread</c> (marshaling the background-thread
/// <see cref="IMessageTransport.EnvelopeReceived"/> event onto the UI thread — the same
/// light-touch-directly-in-the-main-partial precedent <see cref="DocumentViewerViewModel"/> already
/// sets for its own <c>IDispatcherTimer</c>-driven watermark) and <c>Shell.Current.GoToAsync</c> in
/// <see cref="OpenAttachmentAsync"/>, mirroring <c>LibraryViewModel.OpenFileAsync</c>. Picking a file
/// to attach (browsing the library / uploading a new one) needs <c>DisplayActionSheet</c>/<c>FilePicker</c>,
/// which — per this codebase's established convention — live in <see cref="Views.ChatPage"/>'s
/// code-behind instead; it just calls <see cref="SetPendingAttachment"/> with the result.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject, IQueryAttributable
{
    private readonly IChatSessionRepository _sessionRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessagingService _messagingService;
    private readonly IMessageTransport _messageTransport;
    private readonly ISharedLibraryService _libraryService;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IContactDirectoryService _contactDirectoryService;
    private readonly IDiagnosticsReporter _diagnosticsReporter;

    private Guid _chatSessionId;
    private EventHandler<MessageEnvelope>? _envelopeReceivedHandler;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ChatMessageItem> Messages { get; set; }

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

    /// <summary>Set by <see cref="Views.ChatPage"/>'s attach flow (library pick or upload-then-attach); attached to the next message sent, then cleared.</summary>
    [ObservableProperty]
    public partial Guid? PendingAttachmentLibraryFileId { get; set; }

    [ObservableProperty]
    public partial string? PendingAttachmentFileName { get; set; }

    [ObservableProperty]
    public partial bool HasPendingAttachment { get; set; }

    public ChatViewModel(
        IChatSessionRepository sessionRepository,
        IMessageRepository messageRepository,
        IMessagingService messagingService,
        IMessageTransport messageTransport,
        ISharedLibraryService libraryService,
        ITransportSettingsRepository transportSettingsRepository,
        ICurrentUserService currentUserService,
        IContactDirectoryService contactDirectoryService,
        IDiagnosticsReporter diagnosticsReporter)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _contactDirectoryService = contactDirectoryService ?? throw new ArgumentNullException(nameof(contactDirectoryService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));

        Title = "Chat"; // "Chat" is used identically in Czech, kept as-is
        Messages = [];
        ComposeText = string.Empty;
    }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Error, value!, nameof(ChatViewModel));
    }

    partial void OnComposeTextChanged(string value) => RecomputeCanSend();

    partial void OnPendingAttachmentFileNameChanged(string? value)
    {
        HasPendingAttachment = !string.IsNullOrEmpty(value);
        RecomputeCanSend();
    }

    private void RecomputeCanSend() => CanSend = !string.IsNullOrWhiteSpace(ComposeText) || HasPendingAttachment;

    /// <summary>Called from <see cref="Views.ChatPage"/>'s code-behind once a library file has been picked or uploaded.</summary>
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
        if (query.TryGetValue("chatSessionId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _chatSessionId = id;
    }

    /// <summary>
    /// Best-effort reconnect on opening a chat — the user's real, live complaint (2026-09-05):
    /// a WebSocket connection that dropped mid-session (a relay restart, a network blip) never
    /// reconnects on its own; only <c>App.xaml.cs</c>'s launch-time auto-connect existed before
    /// this, so a message silently stayed Pending with no obvious reason why until someone
    /// happened to check Settings. Mirrors that same method's logic (including respecting
    /// <c>IsAutoConnectEnabled</c>, so a user who deliberately turned auto-connect off isn't
    /// overridden just because they opened a chat) rather than a new always-on behavior.
    /// </summary>
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
            // Best-effort — SendAsync already tolerates staying Pending if this doesn't pan out.
        }
    }

    /// <summary>Raised once the thread has been (re)populated so the hosting view can scroll to the newest message — see ChatThreadView's own subscription. Kept as a plain event (not a bound property) since "scroll now" is a one-shot action, not state.</summary>
    public event Action? ScrollToBottomRequested;

    /// <summary>How many of the newest messages to decrypt and show immediately on open (2026-09-11 perf, the user's own ask: "nemusí se načíst celý chat ale třeba jen posledních 5-10 zpráv"). The rest are decrypted and prepended in the background — see LoadOlderInBackgroundAsync.</summary>
    private const int InitialMessageCount = 12;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            // 2026-09-11 perf: render only the NEWEST few messages from local storage FIRST, before
            // any network call and without decrypting the whole thread. Opening a chat used to await
            // a relay connect AND a directory fetch AND decrypt every message before showing a single
            // one — so on a slow link a long thread sat blank behind a spinner. Now: decrypt just the
            // last InitialMessageCount, show them, and push both the older messages and all network
            // work (connect, name refresh, key offer) to the background.
            await _currentUserService.InitializeAsync(); // in-memory-cached after first call; needed for delete gating
            var session = await _sessionRepository.GetByIdAsync(_chatSessionId);

            // Show the name we already have locally immediately; the directory may refine it later.
            Title = session?.PeerDisplayName ?? "Chat";

            var currentRole = _currentUserService.Current.Role;

            // Cheap: filter + order by the row metadata only (no decryption yet).
            var allRows = await _messageRepository.GetBySessionAsync(_chatSessionId);
            var visible = allRows
                .Where(m => m.GroupChatId is null && !m.IsSystemPayload) // 1:1 only; system payloads never shown — see the receive path's own remarks
                .OrderBy(m => m.CreatedAtUtc)
                .ToList();

            var initialStart = Math.Max(0, visible.Count - InitialMessageCount);
            var initialRows = visible.Skip(initialStart).ToList();
            var olderRows = visible.Take(initialStart).ToList();

            // Decrypt off the UI thread so the per-message crypto never stutters the open/scroll
            // animation (2026-09-11 — the user reported jank on an S23+). The continuation resumes on
            // the UI thread, where the bound collection must be assigned.
            var initialItems = await Task.Run(async () =>
            {
                var list = new List<ChatMessageItem>(initialRows.Count);
                foreach (var message in initialRows)
                    list.Add(await BuildItemAsync(message, currentRole));
                return list;
            });

            Messages = new ObservableCollection<ChatMessageItem>(initialItems);
            IsLoading = false; // newest messages are on screen now — stop the spinner
            ScrollToBottomRequested?.Invoke();

            // Older messages + all network work, off the critical path.
            _ = LoadOlderInBackgroundAsync(olderRows, currentRole);
            _ = RefreshInBackgroundAsync(session);
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se načíst tento chat: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Builds one thread item from an already-loaded message row (decrypt + delete-gating) — shared by the initial load, the background older-message load, and the live receive path.</summary>
    private async Task<ChatMessageItem> BuildItemAsync(Message message, Role currentRole)
    {
        var text = await TryDecryptAsync(message);
        var isOwn = message.Direction == MessageDirection.Outbound;
        var canDelete = RoleAccessPolicy.CanDeleteMessage(currentRole, isOwn, message.SenderRole);
        return new ChatMessageItem(message.Id, isOwn, text, message.CreatedAtUtc, message.Status, message.AttachmentLibraryFileId, message.AttachmentFileName, canDelete, message.OriginMessageId);
    }

    /// <summary>
    /// Decrypts the older (already-on-screen-scroll-up) messages after the newest batch is visible,
    /// then prepends them (2026-09-11 perf). The CollectionView's KeepLastItemInView keeps the newest
    /// message anchored while these insert above the viewport, so scrolling up reveals full history
    /// without the open ever having waited for it. A no-op when the thread is short enough to fit the
    /// initial batch.
    /// </summary>
    private async Task LoadOlderInBackgroundAsync(List<Message> olderRows, Role currentRole)
    {
        if (olderRows.Count == 0) return;

        try
        {
            // Decrypt off the UI thread (see the initial batch's own remarks), then marshal only the
            // cheap collection inserts back onto it.
            var olderItems = await Task.Run(async () =>
            {
                var list = new List<ChatMessageItem>(olderRows.Count);
                foreach (var message in olderRows)
                    list.Add(await BuildItemAsync(message, currentRole));
                return list;
            });

            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Insert oldest-first at the front so final order stays chronological.
                for (var i = olderItems.Count - 1; i >= 0; i--)
                    Messages.Insert(0, olderItems[i]);
            });
        }
        catch
        {
            // Best-effort — the newest messages are already usable; a failure here just means
            // scroll-up history isn't backfilled this open, not a broken chat.
        }
    }

    /// <summary>
    /// Everything that needs the network, run AFTER the thread is already visible (2026-09-11 perf):
    /// reconnect the relay, refine the peer's title from the current directory, and best-effort offer
    /// the shared library key. None of this blocks the messages appearing; each is independently
    /// best-effort so a slow or failed relay never leaves the chat unusable.
    /// </summary>
    private async Task RefreshInBackgroundAsync(ChatSession? session)
    {
        await EnsureConnectedAsync();

        if (session is not null)
        {
            try
            {
                // 2026-09-09: prefer the peer's CURRENT name from the relay directory over whatever
                // got captured once at pairing time — see DirectoryNameResolver's own remarks.
                var names = await DirectoryNameResolver.BuildAsync(_contactDirectoryService);
                Title = DirectoryNameResolver.Resolve(names, session.PeerIdentityPublicKey, session.PeerDisplayName);
            }
            catch
            {
                // Keep the locally-stored name already shown — a directory fetch failing must not
                // blank the title or surface an error on an otherwise-usable chat.
            }

            // Best-effort shared-library-key offer (2026-09-10) — see SharedLibraryKeySync's own
            // remarks. Fired on open (not just at pairing time) so a session paired before this
            // mechanism existed still gets the key; its own cooldown keeps repeated opens cheap.
            _ = SharedLibraryKeySync.OfferKeyAsync(_libraryService, _messagingService, _messageTransport, session.Id, _diagnosticsReporter);
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

        try
        {
            var plaintext = Encoding.UTF8.GetBytes(text);
            Message message;
            MessageEnvelope envelope;
            try
            {
                (message, envelope) = await _messagingService.SendMessageAsync(_chatSessionId, plaintext, attachmentLibraryFileId: attachmentId, attachmentFileName: attachmentName);
            }
            catch (ChatSessionClosedException)
            {
                // The session broke earlier (a decrypt-failure auto-heal, or a manual "↺" reset) but
                // nothing had re-paired since — resync right now and retry this exact send once
                // (2026-09-07), instead of just failing and making the user notice and act.
                (message, envelope) = await ResyncAndRetrySendAsync(plaintext, attachmentId, attachmentName);
            }

            // A just-sent message is always the actor's own, so it's always deletable by them (every role may delete its own).
            Messages.Add(new ChatMessageItem(message.Id, true, text, message.CreatedAtUtc, message.Status, message.AttachmentLibraryFileId, message.AttachmentFileName, CanDelete: true, CorrelationId: message.OriginMessageId));
            ScrollToBottomRequested?.Invoke();

            // Best-effort live send: the message is already durably persisted as Pending above
            // regardless of what happens here — no relay connected yet is the expected common
            // case until Milestone 5's transport is actually deployed, not an error condition.
            // Also covers a connection that dropped while this chat was already open (LoadAsync's
            // EnsureConnectedAsync only runs once, on appearing) — cheap no-op if already connected.
            await EnsureConnectedAsync();
            if (_messageTransport.IsConnected)
            {
                try
                {
                    await _messageTransport.SendEnvelopeAsync(envelope);
                    message.MarkSent();
                    await _messageRepository.UpdateAsync(message);
                    ReplaceItem(message.Id, item => item with { Status = message.Status });
                }
                catch (Exception)
                {
                    // Stays Pending in storage — nothing further to do here.
                }
            }
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se odeslat: {ex.Message}";
        }
    }

    /// <summary>Resyncs this thread's peer (see <see cref="SessionRecoveryHelper.ResyncAsync"/>) and retries the same send once against the fresh session — <see cref="_chatSessionId"/> is repointed at the new session id so the rest of this ViewModel (loading, listening) keeps working transparently.</summary>
    private async Task<(Message, MessageEnvelope)> ResyncAndRetrySendAsync(ReadOnlyMemory<byte> plaintext, Guid? attachmentId, string? attachmentName)
    {
        var session = await _sessionRepository.GetByIdAsync(_chatSessionId)
            ?? throw new ChatSessionNotFoundException(_chatSessionId);
        if (session.PeerRelayDeviceId is not { } relayDeviceId)
            throw new InvalidOperationException($"S {session.PeerDisplayName} chybí propojení na relay zařízení — obnovit spojení nelze.");

        await SessionRecoveryHelper.ResyncAsync(
            _messagingService, _messageTransport, _transportSettingsRepository, _currentUserService,
            session.PeerDisplayName, session.PeerIdentityPublicKey, relayDeviceId);

        var newSession = await _sessionRepository.GetByPeerPublicKeyAsync(session.PeerIdentityPublicKey)
            ?? throw new InvalidOperationException("Obnovení spojení se nezdařilo.");
        _chatSessionId = newSession.Id;

        return await _messagingService.SendMessageAsync(_chatSessionId, plaintext, attachmentLibraryFileId: attachmentId, attachmentFileName: attachmentName);
    }

    /// <summary>
    /// Auto-heal (2026-09-07): triggered from <see cref="HandleEnvelopeReceivedAsync"/>'s catch block
    /// on a genuine ratchet decrypt failure — resyncs the session automatically, no button press
    /// required. That one message is unrecoverable either way (Double Ratchet forward secrecy — see
    /// <c>MessagingService.ReceiveMessageAsync</c>'s own remarks), but future messages should go
    /// through once this completes.
    /// </summary>
    private async Task TryAutoHealAsync(Exception originalError)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(_chatSessionId);
            if (session?.PeerRelayDeviceId is not { } relayDeviceId)
            {
                StatusErrorMessage = $"Nepodařilo se zpracovat příchozí zprávu: {originalError.Message}";
                return;
            }

            await SessionRecoveryHelper.ResyncAsync(
                _messagingService, _messageTransport, _transportSettingsRepository, _currentUserService,
                session.PeerDisplayName, session.PeerIdentityPublicKey, relayDeviceId);
            StatusErrorMessage = $"Spojení s {session.PeerDisplayName} se automaticky obnovilo na pozadí. Tahle jedna zpráva se ztratila, další už by měly projít v pořádku.";
        }
        catch (Exception healEx)
        {
            StatusErrorMessage = $"Zprávu se nepodařilo zpracovat a automatické obnovení spojení selhalo: {healEx.Message}";
        }
    }

    private async Task<string> TryDecryptAsync(Message message)
    {
        try
        {
            var plaintext = await _messagingService.DecryptMessageAsync(message); // already-loaded payload, no re-fetch
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception)
        {
            return "[Tuto zprávu se nepodařilo dešifrovat]";
        }
    }

    private void ReplaceItem(Guid messageId, Func<ChatMessageItem, ChatMessageItem> update)
    {
        for (var i = 0; i < Messages.Count; i++)
        {
            if (Messages[i].Id == messageId)
            {
                Messages[i] = update(Messages[i]);
                return;
            }
        }
    }

    /// <summary>
    /// Handles a received envelope for this session — the real logic, directly callable from a
    /// console test without a live MAUI app. The real event subscription (<see cref="StartListening"/>)
    /// is a thin <c>MainThread</c> wrapper around this.
    /// </summary>
    internal async Task HandleEnvelopeReceivedAsync(MessageEnvelope envelope)
    {
        if (envelope.SessionId != _chatSessionId) return;

        // Group chats (2026-09-07) fan out over this SAME pairwise session — see GroupChat's own
        // remarks on the crypto design — so a group envelope also matches the check above and would
        // otherwise reach here too. It must not: GroupChatViewModel.HandleEnvelopeReceivedAsync
        // already decrypts it (a Double Ratchet message key is one-time-use — decrypting the same
        // envelope twice fails the second time, surfacing as "message decryption failed" whenever a
        // direct 1:1 thread with that peer happens to be open at the same time a group message from
        // them arrives — a real bug caught live, not a hypothetical). Group traffic belongs in the
        // group's own thread only.
        if (envelope.GroupChatId is not null) return;

        // System-carried machinery (2026-09-10) — see the LoadAsync filter's own remarks just above.
        // App.OnEnvelopeReceived's app-wide handler decrypts-and-processes these too; here we ALSO
        // handle a delete command (2026-09-11) so the visible thread updates live when a page is open,
        // then stop — a key offer/request has nothing to show and is left to the app-wide handler.
        if (envelope.IsSystemPayload)
        {
            await HandleSystemPayloadAsync(envelope);
            return;
        }

        try
        {
            var message = await _messagingService.ReceiveMessageAsync(envelope);
            var text = await TryDecryptAsync(message);
            var canDelete = RoleAccessPolicy.CanDeleteMessage(_currentUserService.Current.Role, false, message.SenderRole);
            Messages.Add(new ChatMessageItem(message.Id, false, text, message.CreatedAtUtc, message.Status, message.AttachmentLibraryFileId, message.AttachmentFileName, canDelete, message.OriginMessageId));
            ScrollToBottomRequested?.Invoke();
        }
        catch (Exception ex)
        {
            await TryAutoHealAsync(ex);
        }
    }

    /// <summary>Handles a system payload live while this thread is open — currently just a delete command (removes the target from the visible list; the DB delete is idempotent and also done by the app-wide handler). Best-effort; a key offer/request falls through as a no-op here.</summary>
    private async Task HandleSystemPayloadAsync(MessageEnvelope envelope)
    {
        try
        {
            var message = await _messagingService.ReceiveMessageAsync(envelope); // idempotent — safe alongside the app-wide handler
            var plaintext = await _messagingService.DecryptMessageAsync(message.Id);
            if (MessageDeletionSync.TryParseDeleteCommand(plaintext, out var correlationId))
            {
                await _messageRepository.DeleteByCorrelationAsync(correlationId);
                var doomed = Messages.Where(m => m.CorrelationId == correlationId).ToList();
                foreach (var item in doomed)
                    Messages.Remove(item);
            }
        }
        catch
        {
            // Best-effort — a malformed/foreign system payload never disrupts the open thread.
        }
    }

    /// <summary>Call from the page's OnAppearing (paired with <see cref="StopListening"/> in OnDisappearing) — subscribes to a Singleton service's event, so it must be unsubscribed or every page visit leaks a handler.</summary>
    public void StartListening()
    {
        if (_envelopeReceivedHandler is not null) return; // already subscribed — OnAppearing can re-fire on a page popped back to

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

    /// <summary>Downloads + decrypts + imports the attachment via the shared library (same "become a normal local Document, open in the existing viewer" path as <c>LibraryViewModel.OpenFileAsync</c>), then opens it.</summary>
    [RelayCommand]
    private async Task OpenAttachmentAsync(ChatMessageItem? item)
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

    /// <summary>
    /// Deletes a message for everyone (2026-09-11) — RBAC-gated (see <c>RoleAccessPolicy.CanDeleteMessage</c>:
    /// Admin any, Modifier own + Viewers', Viewer own only). Re-checks the policy here even though the
    /// UI only shows the option when allowed, since the item's <c>CanDelete</c> is a snapshot. Removes
    /// this device's local copy and, if the message has a cross-device correlation id, propagates a
    /// delete command to the peer over the same ratchet; a message too old to carry one is removed
    /// locally only.
    /// </summary>
    [RelayCommand]
    private async Task DeleteMessageAsync(ChatMessageItem? item)
    {
        if (item is null) return;

        var message = await _messageRepository.GetByIdAsync(item.Id);
        if (message is null)
        {
            // Already gone locally (e.g. a peer's delete raced this) — just drop it from the list.
            var stale = Messages.FirstOrDefault(m => m.Id == item.Id);
            if (stale is not null) Messages.Remove(stale);
            return;
        }

        var isOwn = message.Direction == MessageDirection.Outbound;
        if (!RoleAccessPolicy.CanDeleteMessage(_currentUserService.Current.Role, isOwn, message.SenderRole))
        {
            StatusErrorMessage = "Na smazání této zprávy nemáte oprávnění.";
            return;
        }

        var confirmed = await Shell.Current.DisplayAlert("Smazat zprávu", "Opravdu smazat tuto zprávu? Smaže se u všech účastníků.", "Smazat", "Zrušit");
        if (!confirmed) return;

        try
        {
            var correlationId = message.OriginMessageId;
            if (correlationId is { } corr)
                await _messageRepository.DeleteByCorrelationAsync(corr);
            else
                await _messageRepository.DeleteAsync(message.Id);

            var existing = Messages.FirstOrDefault(m => m.Id == item.Id);
            if (existing is not null) Messages.Remove(existing);

            // Propagate the delete to the peer — only possible for a message carrying a cross-device
            // correlation id (a very old message can't be deleted for the other side, only locally).
            if (correlationId is { } corr2)
            {
                var payload = MessageDeletionSync.BuildDeleteCommand(corr2);
                var (_, envelope) = await _messagingService.SendMessageAsync(_chatSessionId, payload, isSystemPayload: true);
                await EnsureConnectedAsync();
                if (_messageTransport.IsConnected)
                    await _messageTransport.SendEnvelopeAsync(envelope);
            }
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Zprávu se nepodařilo smazat: {ex.Message}";
        }
    }
}

public sealed record ChatMessageItem(Guid Id, bool IsOutbound, string Text, DateTimeOffset SentAtUtc, MessageStatus Status, Guid? AttachmentLibraryFileId = null, string? AttachmentFileName = null, bool CanDelete = false, Guid? CorrelationId = null)
{
    public bool HasAttachment => AttachmentLibraryFileId is not null;
    public bool HasText => !string.IsNullOrEmpty(Text);
}
