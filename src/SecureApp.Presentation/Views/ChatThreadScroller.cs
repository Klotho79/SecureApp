using System.Windows.Input;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>
/// The scroll behaviour 1:1 (<see cref="ChatThreadView"/>) and group (<see cref="GroupChatThreadView"/>)
/// threads share (2026-11-06, one copy instead of two): jump to the newest message when the ViewModel
/// asks, re-anchor after a page of older history was prepended, and load the next older page on a
/// genuine upward scroll to the top. The host view calls <see cref="Attach"/> from
/// <c>OnBindingContextChanged</c>; the previous ViewModel is unsubscribed first so reusing the view for
/// another chat never leaves a handler firing on the wrong thread.
/// </summary>
internal sealed class ChatThreadScroller<TItem> where TItem : class
{
    private readonly CollectionView _messagesView;
    private readonly Func<ICommand?> _olderPageCommand;
    private ChatThreadViewModelBase<TItem>? _viewModel;

    /// <param name="messagesView">The thread's message list.</param>
    /// <param name="olderPageCommand">Returns the "load older page" command when there is an older page and it can run now, otherwise null.</param>
    public ChatThreadScroller(CollectionView messagesView, Func<ICommand?> olderPageCommand)
    {
        _messagesView = messagesView;
        _olderPageCommand = olderPageCommand;
        _messagesView.Scrolled += OnMessagesScrolled;
    }

    public void Attach(ChatThreadViewModelBase<TItem>? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToBottomRequested -= ScrollToLatest;
            _viewModel.ScrollAnchorRequested -= ScrollToAnchor;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToBottomRequested += ScrollToLatest;
            _viewModel.ScrollAnchorRequested += ScrollToAnchor;
        }
    }

    /// <summary>Always jump to the newest message. Marshaled to the UI thread (the event can be raised from a background continuation), guarded for an empty thread, and not animated: on open it should just be there.</summary>
    private void ScrollToLatest()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var messages = _viewModel?.Messages;
            if (messages is null || messages.Count == 0) return;
            try { _messagesView.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false); }
            catch { /* a layout not ready yet is harmless — KeepLastItemInView still covers new items */ }
        });
    }

    /// <summary>After a page of older history is prepended, re-anchor on the message that was at the top so the view doesn't jump to the oldest message.</summary>
    private void ScrollToAnchor(TItem anchor)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { _messagesView.ScrollTo(anchor, position: ScrollToPosition.Start, animate: false); }
            catch { /* layout not ready — harmless */ }
        });
    }

    /// <summary>
    /// Scroll-up history paging: load the next older page only on a genuine UPWARD scroll that reaches the
    /// top. Requiring VerticalDelta &lt; 0 is what stops it from firing during the initial layout / the
    /// programmatic scroll-to-newest on open, which reported the top momentarily and used to auto-prepend
    /// history and yank the view.
    /// </summary>
    private void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (e.VerticalDelta < 0 && e.FirstVisibleItemIndex <= 2 && _olderPageCommand() is { } load)
            load.Execute(null);
    }
}
