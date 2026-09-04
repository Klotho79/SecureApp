using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class NewChatPage : ContentPage
{
    private readonly NewChatViewModel _viewModel;

    public NewChatPage(NewChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }
}
