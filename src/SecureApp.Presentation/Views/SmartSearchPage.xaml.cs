using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class SmartSearchPage : ContentPage
{
    private readonly SmartSearchViewModel _viewModel;

    public SmartSearchPage(SmartSearchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RequestNavigate += OnRequestNavigate;
    }

    private async void OnRequestNavigate(string route) => await Shell.Current.GoToAsync(route);
}
