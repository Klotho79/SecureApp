using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
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
        ISharedLibraryService libraryService)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));

        Title = "Chat";
        Messages = [];
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

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            var session = await _sessionRepository.GetByIdAsync(_chatSessionId);
            Title = session?.PeerDisplayName ?? "Chat";

            var messages = await _messageRepository.GetBySessionAsync(_chatSessionId);
            var items = new List<ChatMessageItem>();
            foreach (var message in messages)
            {
                var text = await TryDecryptAsync(message);
                items.Add(new ChatMessageItem(message.Id, message.Direction == MessageDirection.Outbound, text, message.CreatedAtUtc, message.Status, message.AttachmentLibraryFileId, message.AttachmentFileName));
            }

            Messages = new ObservableCollection<ChatMessageItem>(items.OrderBy(m => m.SentAtUtc));
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not load this chat: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
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
            var (message, envelope) = await _messagingService.SendMessageAsync(_chatSessionId, plaintext, attachmentLibraryFileId: attachmentId, attachmentFileName: attachmentName);

            Messages.Add(new ChatMessageItem(message.Id, true, text, message.CreatedAtUtc, message.Status, message.AttachmentLibraryFileId, message.AttachmentFileName));

            // Best-effort live send: the message is already durably persisted as Pending above
            // regardless of what happens here — no relay connected yet is the expected common
            // case until Milestone 5's transport is actually deployed, not an error condition.
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
            StatusErrorMessage = $"Could not send: {ex.Message}";
        }
    }

    private async Task<string> TryDecryptAsync(Message message)
    {
        try
        {
            var plaintext = await _messagingService.DecryptMessageAsync(message.Id);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception)
        {
            return "[Could not decrypt this message]";
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

        try
        {
            var message = await _messagingService.ReceiveMessageAsync(envelope);
            var text = await TryDecryptAsync(message);
            Messages.Add(new ChatMessageItem(message.Id, false, text, message.CreatedAtUtc, message.Status, message.AttachmentLibraryFileId, message.AttachmentFileName));
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not process an incoming message: {ex.Message}";
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
            StatusErrorMessage = $"Could not open '{item.AttachmentFileName}': {ex.Message}";
        }
    }
}

public sealed record ChatMessageItem(Guid Id, bool IsOutbound, string Text, DateTimeOffset SentAtUtc, MessageStatus Status, Guid? AttachmentLibraryFileId = null, string? AttachmentFileName = null)
{
    public bool HasAttachment => AttachmentLibraryFileId is not null;
    public bool HasText => !string.IsNullOrEmpty(Text);
}
