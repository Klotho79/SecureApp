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

        // Load immediately — the view model shows any cached copy synchronously and runs the load in
        // parallel with the slide, applying after the animation settles. Unified with GroupChatPage.
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopListening();
    }
}
