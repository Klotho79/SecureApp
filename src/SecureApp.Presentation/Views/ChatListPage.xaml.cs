using Microsoft.Extensions.DependencyInjection;
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
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _activeThreadViewModel?.StopListening();
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
            _viewModel.OpenSessionCommand.Execute(session);
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

    /// <summary>Group chats (2026-09-07) always push <see cref="Views.GroupChatPage"/> — not folded into the adaptive list+detail split above, a scope call for this first pass (see the XAML's own remarks), not a technical limitation.</summary>
    private void OnGroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        GroupsView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is GroupChatListItem group)
            _viewModel.OpenGroupCommand.Execute(group);
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
}
