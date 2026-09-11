using Microsoft.Maui.Dispatching;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>Narrow/phone-width host for <see cref="ChatThreadView"/> — just the Shell push navigation lifecycle. See ChatThreadView's own remarks for why the actual UI lives there instead of here now.</summary>
public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;

    public ChatPage(ChatViewModel viewModel, ISharedLibraryService libraryService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        Content = new ChatThreadView(libraryService) { BindingContext = viewModel };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.StartListening();

        // Defer the load until the push animation finishes so populating the thread doesn't jank the
        // slide-in (2026-09-11) — see GroupChatPage.OnAppearing's own remarks. The spinner is only
        // shown if the load runs long (see LoadAsync's delayed spinner), so a fast open is
        // spinner-free. Only affects the phone push host; the wide-layout detail pane drives its own load.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => _viewModel.LoadCommand.Execute(null));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopListening();
    }
}
