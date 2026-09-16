using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>
/// Adaptive list+detail split — see the XAML's own remarks for why (a real Windows Shell
/// back-button gap the user caught live, 2026-09-06). <see cref="_wideLayoutThreshold"/> is this
/// page's own guess at "wide enough to show both panes"; MAUI has no built-in breakpoint concept
/// to defer to.
/// </summary>
public partial class ChatListPage : ContentPage
{
    private const double _wideLayoutThreshold = 760;

    private readonly ChatListViewModel _viewModel;
    private readonly IServiceProvider _services;

    private bool _isWideLayout;
    private ChatViewModel? _activeThreadViewModel;

    // Persistent phone thread hosting (2026-09-13): reuse ONE view per conversation (shown/hidden in
    // NarrowThreadOverlay) instead of pushing/popping a page — so reopening a chat doesn't rebuild +
    // re-render it (the ~74ms MAUI floor). Small LRU per kind so memory stays bounded. 1:1 and group
    // both host into the same overlay; the active listener is tracked via start/stop delegates so the
    // overlay code doesn't care which VM type is showing. See ShowNarrowThread / ShowNarrowGroup.
    private readonly LinkedList<(Guid Id, ChatThreadView View, ChatViewModel Vm)> _narrowThreads = new();
    private readonly LinkedList<(Guid Id, GroupChatThreadView View, GroupChatViewModel Vm)> _narrowGroups = new();
    private const int _maxCachedNarrowThreads = 4;
    private Action? _activeNarrowStop;
    private Action? _activeNarrowResume;
    /// <summary>Set only while a group is the active narrow thread (2026-09-16) — lets OnNarrowGroupLeaveClicked reuse GroupChatThreadView's own confirmed-leave flow instead of duplicating its dialog text.</summary>
    private GroupChatThreadView? _activeNarrowGroupView;

    public ChatListPage(ChatListViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        SizeChanged += OnPageSizeChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Defer the refresh until the (back-)navigation animation has finished (2026-09-11). This
        // page persists as a tab root, so its existing list is still on screen and slides in smoothly
        // while closing a chat; reloading it during that slide was what janked the close. The list's
        // current content stays visible, then refreshes once the animation is done.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(280), () => _viewModel.LoadCommand.Execute(null));

        // Returning to the Chats tab with a chat still open in the overlay: resume its live listener
        // (it was stopped in OnDisappearing) so new messages arrive live again. The view itself stayed
        // built, so this is just re-subscribing, no rebuild.
        if (NarrowThreadOverlay.IsVisible)
            _activeNarrowResume?.Invoke();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _activeThreadViewModel?.StopListening();
        // Leaving the Chats tab: stop the open narrow thread's live listener too (the overlay stays
        // visible so returning to the tab shows the same chat instantly — App-level OnEnvelopeReceived
        // still persists any messages that arrive meanwhile; the next open reloads them).
        _activeNarrowStop?.Invoke();
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        var wide = Width >= _wideLayoutThreshold;
        if (wide == _isWideLayout) return;
        _isWideLayout = wide;

        if (wide)
        {
            ListColumn.Width = new GridLength(380, GridUnitType.Absolute);
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            DetailPane.IsVisible = true;
        }
        else
        {
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            DetailColumn.Width = new GridLength(0, GridUnitType.Absolute);
            DetailPane.IsVisible = false;
        }
    }

    /// <summary>
    /// Wide layout: resolves a fresh ChatViewModel from DI and hosts it in DetailPane directly,
    /// manually driving the Load/StartListening lifecycle ChatPage's OnAppearing would otherwise
    /// handle (this view is never a Page, so it never gets that callback). Narrow layout: unchanged
    /// push-navigation to ChatPage, same as before this pass.
    /// </summary>
    private void OnSessionSelected(object? sender, SelectionChangedEventArgs e)
    {
        SessionsView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not ChatSessionItem session) return;

        if (!_isWideLayout)
        {
            ShowNarrowThread(session.Id);
            return;
        }

        _activeThreadViewModel?.StopListening();

        var threadViewModel = _services.GetRequiredService<ChatViewModel>();
        threadViewModel.ApplyQueryAttributes(new Dictionary<string, object> { ["chatSessionId"] = session.Id.ToString() });

        var thread = new ChatThreadView(_services.GetRequiredService<ISharedLibraryService>())
        {
            BindingContext = threadViewModel
        };

        ThreadHost.Content = thread;
        ThreadHost.IsVisible = true;
        EmptyDetailState.IsVisible = false;
        _activeThreadViewModel = threadViewModel;

        threadViewModel.LoadCommand.Execute(null);
        threadViewModel.StartListening();
    }

    /// <summary>
    /// Phone path (2026-09-13): show the chat in a persistent, reused ChatThreadView inside
    /// NarrowThreadOverlay instead of pushing a page. Reopening the same chat reuses the already-built
    /// view (its CollectionView cells are still realized) so there is NO page rebuild/re-render — the
    /// win the user's own observation pointed to (the chat LIST is fast because it's persistent).
    /// </summary>
    private void ShowNarrowThread(Guid sessionId)
    {
        _activeNarrowStop?.Invoke();

        // Reuse the cached view for this session if we have it; otherwise build one and cache it (LRU).
        ChatThreadView view;
        ChatViewModel vm;
        var existing = _narrowThreads.FirstOrDefault(t => t.Id == sessionId);
        if (existing.View is not null)
        {
            view = existing.View;
            vm = existing.Vm;
            _narrowThreads.Remove(existing);
            _narrowThreads.AddFirst(existing);
        }
        else
        {
            vm = _services.GetRequiredService<ChatViewModel>();
            vm.ApplyQueryAttributes(new Dictionary<string, object> { ["chatSessionId"] = sessionId.ToString() });
            view = new ChatThreadView(_services.GetRequiredService<ISharedLibraryService>()) { BindingContext = vm };
            _narrowThreads.AddFirst((sessionId, view, vm));
            while (_narrowThreads.Count > _maxCachedNarrowThreads) _narrowThreads.RemoveLast();
        }

        if (!ReferenceEquals(NarrowThreadHost.Content, view))
            NarrowThreadHost.Content = view;
        NarrowThreadOverlay.IsVisible = true;
        NarrowGroupHeaderExtras.IsVisible = false;
        _activeNarrowGroupView = null;
        _activeNarrowStop = vm.StopListening;
        _activeNarrowResume = vm.StartListening;

        vm.StartListening();
        vm.LoadCommand.Execute(null); // fast: reused view skips the rebuild when content is unchanged
    }

    /// <summary>Group counterpart of <see cref="ShowNarrowThread"/> — hosts a reused GroupChatThreadView in the same overlay so reopening a group is instant.</summary>
    private void ShowNarrowGroup(Guid groupId)
    {
        _activeNarrowStop?.Invoke();

        GroupChatThreadView view;
        GroupChatViewModel vm;
        var existing = _narrowGroups.FirstOrDefault(t => t.Id == groupId);
        if (existing.View is not null)
        {
            view = existing.View;
            vm = existing.Vm;
            _narrowGroups.Remove(existing);
            _narrowGroups.AddFirst(existing);
        }
        else
        {
            vm = _services.GetRequiredService<GroupChatViewModel>();
            vm.ApplyQueryAttributes(new Dictionary<string, object> { ["groupChatId"] = groupId.ToString() });
            view = new GroupChatThreadView(_services.GetRequiredService<ISharedLibraryService>()) { BindingContext = vm };
            // 2026-09-16 — see GroupChatViewModel.LeftGroup's own remarks: this overlay is the
            // non-pushed host, so leaving means hiding it, not a Shell pop. Subscribed once here (not
            // in the "existing" branch above) since this vm is cached/reused across reopens.
            vm.LeftGroup += HideNarrowThread;
            _narrowGroups.AddFirst((groupId, view, vm));
            while (_narrowGroups.Count > _maxCachedNarrowThreads) _narrowGroups.RemoveLast();
        }

        if (!ReferenceEquals(NarrowThreadHost.Content, view))
            NarrowThreadHost.Content = view;
        NarrowThreadOverlay.IsVisible = true;

        // Compact header (2026-09-16, user's own ask): this view's own Members/Add/Leave row is
        // hidden and the SAME controls appear instead on the overlay's "‹ Zpět" row — see
        // GroupChatThreadView.SetCompactHeaderHosted's own remarks.
        view.SetCompactHeaderHosted(true);
        NarrowGroupHeaderExtras.BindingContext = vm;
        NarrowGroupHeaderExtras.IsVisible = true;
        _activeNarrowGroupView = view;

        _activeNarrowStop = vm.StopListening;
        _activeNarrowResume = vm.StartListening;

        vm.StartListening();
        vm.LoadCommand.Execute(null);
    }

    private void HideNarrowThread()
    {
        _activeNarrowStop?.Invoke();
        _activeNarrowStop = null;
        _activeNarrowResume = null;
        NarrowGroupHeaderExtras.IsVisible = false;
        _activeNarrowGroupView = null;
        NarrowThreadOverlay.IsVisible = false;
    }

    /// <summary>Compact-header counterpart of GroupChatThreadView's own OnLeaveGroupClicked — reuses its public LeaveGroupAsync so the confirm dialog text lives in exactly one place.</summary>
    private async void OnNarrowGroupLeaveClicked(object? sender, EventArgs e)
    {
        if (_activeNarrowGroupView is { } view)
            await view.LeaveGroupAsync();
    }

    private void OnNarrowThreadBack(object? sender, EventArgs e) => HideNarrowThread();

    /// <summary>Hardware/gesture back closes the open chat overlay (hide, don't destroy — keeps it warm) instead of leaving the Chats tab.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (NarrowThreadOverlay.IsVisible)
        {
            HideNarrowThread();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    /// <summary>Group chats (2026-09-07) always push <see cref="Views.GroupChatPage"/> — not folded into the adaptive list+detail split above, a scope call for this first pass (see the XAML's own remarks), not a technical limitation.</summary>
    private void OnGroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        GroupsView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not GroupChatListItem group) return;

        if (_isWideLayout)
            _viewModel.OpenGroupCommand.Execute(group); // wide/PC: keep push navigation (unchanged)
        else
            ShowNarrowGroup(group.Id); // phone: persistent overlay, no rebuild on reopen
    }

    /// <summary>Confirmation dialog lives here per this codebase's established "native prompts live in the page code-behind" convention — <see cref="ChatListViewModel.ResetSessionAsync"/> does the actual resync once confirmed (2026-09-07: a single tap now fully re-pairs on its own, nothing to do on the other device).</summary>
    private async void OnResetSessionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: ChatSessionItem session }) return;

        var confirmed = await DisplayAlertAsync(
            "Obnovit spojení",
            $"Obnovit spojení s '{session.PeerDisplayName}'? Historie zpráv zůstane zachovaná. Appka se s ním rovnou znovu spáruje sama — na jeho zařízení není potřeba dělat nic.",
            "Obnovit spojení",
            "Storno");
        if (!confirmed) return;

        await _viewModel.ResetSessionCommand.ExecuteAsync(session);

        if (_viewModel.ResetErrorMessage is { } error)
            await DisplayAlertAsync("Obnovení se nezdařilo", error, "OK");
    }

    /// <summary>See <see cref="ChatListViewModel.DeleteSessionAsync"/>'s own remarks on why this is local-only, not a two-sided unpair — the confirmation text below says so explicitly rather than leaving it implicit.</summary>
    private async void OnDeleteSessionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: ChatSessionItem session }) return;

        var confirmed = await DisplayAlertAsync(
            "Smazat chat",
            $"Smazat chat s '{session.PeerDisplayName}' jen z tohoto zařízení? Historie zpráv tady zmizí. Pokud vám tato osoba znovu napíše, appka se s ní může automaticky znovu spárovat.",
            "Smazat",
            "Storno");
        if (!confirmed) return;

        await _viewModel.DeleteSessionCommand.ExecuteAsync(session);

        if (_viewModel.DeleteErrorMessage is { } error)
            await DisplayAlertAsync("Smazání se nezdařilo", error, "OK");
    }

    /// <summary>See <see cref="ChatListViewModel.DeleteGroupAsync"/>'s own remarks — local-only, distinct from "Opustit" (leave) on <see cref="GroupChatPage"/> itself.</summary>
    private async void OnDeleteGroupClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: GroupChatListItem group }) return;

        var confirmed = await DisplayAlertAsync(
            "Smazat skupinu",
            $"Smazat skupinu '{group.Name}' jen z tohoto zařízení? Historie zpráv tady zmizí, ale pro ostatní členy skupina dál existuje — pokud chcete opravdu vystoupit, otevřete skupinu a použijte '🚪 Opustit'.",
            "Smazat",
            "Storno");
        if (!confirmed) return;

        await _viewModel.DeleteGroupCommand.ExecuteAsync(group);

        if (_viewModel.DeleteErrorMessage is { } error)
            await DisplayAlertAsync("Smazání se nezdařilo", error, "OK");
    }
}
