using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>
/// The actual message-thread + compose-bar UI, extracted out of <see cref="ChatPage"/> (2026-09-06)
/// so the same view can be hosted two ways: as a full pushed page on narrow/phone widths (still
/// <see cref="ChatPage"/>, unchanged behavior) and embedded directly in <see cref="ChatListPage"/>'s
/// detail pane on wide/desktop widths — a real gap the user caught live: Windows' Shell back
/// button doesn't reliably bring you back from a pushed ChatPage to the list (works fine on
/// Android), and a persistent list+thread split sidesteps that entirely rather than chasing the
/// platform bug directly. Does not manage its own ViewModel's Load/StartListening/StopListening
/// lifecycle — whoever hosts this (ChatPage's OnAppearing/OnDisappearing, or ChatListPage's
/// selection-changed/page-lifecycle handling) is responsible for that, same as before.
/// </summary>
public partial class ChatThreadView : ContentView
{
    private readonly ISharedLibraryService _libraryService;

    private readonly ChatThreadScroller<ChatMessageItem> _scroller;

    public ChatThreadView(ISharedLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        // Scroll-up history paging: see ChatViewModel.LoadOlderAsync.
        _scroller = new ChatThreadScroller<ChatMessageItem>(
            MessagesView,
            () => ViewModel is { HasOlderMessages: true } vm && vm.LoadOlderCommand.CanExecute(null) ? vm.LoadOlderCommand : null);
    }

    private ChatViewModel? ViewModel => BindingContext as ChatViewModel;

    /// <summary>
    /// The host (ChatPage, or ChatListPage's detail pane) sets our BindingContext to the ChatViewModel
    /// after construction — and may reuse this view for a different chat — so the scroll subscriptions
    /// are (re)wired here rather than in the constructor.
    /// </summary>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        _scroller?.Attach(ViewModel);
    }

    /// <summary>
    /// Attach flow: either pick an already-uploaded file from the shared library, or pick a local
    /// file and upload it first — either way ends by calling <see cref="ChatViewModel.SetPendingAttachment"/>.
    /// Uses <c>DisplayActionSheet</c>/<c>FilePicker</c> here (not the ViewModel) per this codebase's
    /// established "native prompts live in the page code-behind" convention.
    /// </summary>
    private async void OnAttachClicked(object? sender, EventArgs e) =>
        await ChatAttachmentPicker.ChooseAndAttachAsync(_libraryService, (id, name) => ViewModel?.SetPendingAttachment(id, name));
}
