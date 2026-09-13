using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Shared scaffolding for a message thread — 1:1 (<see cref="ChatViewModel"/>) and group
/// (<see cref="GroupChatViewModel"/>) both derive from this (2026-09-13, the user's ask to stop
/// duplicating "why two chats" code). Holds ONLY the type-independent UI state that was identical in
/// both: the message collection, compose text + send-enable, loading/error state, the pending
/// attachment, the scroll-coordination events, and the paging/settle constants.
///
/// Deliberately does NOT pull up the message loading / sending / deleting / decryption or the group's
/// member-mesh logic: those genuinely differ (different item type, different queries, group-only member
/// management) and forcing them into one class would add branching and risk to the crypto-adjacent
/// path, not remove it. So the shared, safe part lives here once; each subclass keeps its own domain
/// logic. <typeparamref name="TItem"/> is the thread's message-item type (ChatMessageItem /
/// GroupMessageItem), which lets the shared <see cref="Messages"/> collection and
/// <see cref="ScrollAnchorRequested"/> event stay strongly typed.
/// </summary>
public abstract partial class ChatThreadViewModelBase<TItem> : ObservableObject
{
    /// <summary>How many of the newest messages to show immediately on open; older ones load a page at a time on scroll-up.</summary>
    protected const int InitialMessageCount = 8;
    protected const int OlderPageSize = 20;

    /// <summary>Settle delay before touching the UI. 0 now — the chat push no longer animates (see ChatListViewModel), so there is no slide to hold content back from.</summary>
    protected const int AnimationSettleMs = 0;

    protected readonly IDiagnosticsReporter DiagnosticsReporter;

    protected ChatThreadViewModelBase(IDiagnosticsReporter diagnosticsReporter)
    {
        DiagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        Messages = [];
        ComposeText = string.Empty;
    }

    /// <summary>Raised once the thread is (re)populated so the hosting view can scroll to the newest message.</summary>
    public event Action? ScrollToBottomRequested;

    /// <summary>Raised after older history is prepended, carrying the previously-top item so the view can re-anchor (no jump).</summary>
    public event Action<TItem>? ScrollAnchorRequested;

    protected void RaiseScrollToBottom() => ScrollToBottomRequested?.Invoke();
    protected void RaiseScrollToAnchor(TItem anchor) => ScrollAnchorRequested?.Invoke(anchor);

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<TItem> Messages { get; set; }

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

    [ObservableProperty]
    public partial Guid? PendingAttachmentLibraryFileId { get; set; }

    [ObservableProperty]
    public partial string? PendingAttachmentFileName { get; set; }

    [ObservableProperty]
    public partial bool HasPendingAttachment { get; set; }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = DiagnosticsReporter.ReportAsync(DiagnosticLogLevel.Error, value!, GetType().Name);
    }

    partial void OnComposeTextChanged(string value) => RecomputeCanSend();

    partial void OnPendingAttachmentFileNameChanged(string? value)
    {
        HasPendingAttachment = !string.IsNullOrEmpty(value);
        RecomputeCanSend();
    }

    protected void RecomputeCanSend() => CanSend = !string.IsNullOrWhiteSpace(ComposeText) || HasPendingAttachment;

    /// <summary>Called from the page's code-behind once a library file has been picked or uploaded.</summary>
    public void SetPendingAttachment(Guid libraryFileId, string fileName)
    {
        PendingAttachmentLibraryFileId = libraryFileId;
        PendingAttachmentFileName = fileName;
    }

    [RelayCommand]
    protected void ClearPendingAttachment()
    {
        PendingAttachmentLibraryFileId = null;
        PendingAttachmentFileName = null;
    }
}
