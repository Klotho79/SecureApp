using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class ChatListPage : ContentPage
{
    private readonly ChatListViewModel _viewModel;

    public ChatListPage(ChatListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private void OnSessionSelected(object? sender, SelectionChangedEventArgs e)
    {
        SessionsView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is ChatSessionItem session)
            _viewModel.OpenSessionCommand.Execute(session);
    }
}
