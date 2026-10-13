using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class RedakcePage : ContentPage
{
    private readonly RedakceViewModel _viewModel;

    public RedakcePage(RedakceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RequestNavigate += OnRequestNavigate;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private async void OnRequestNavigate(string route) => await Shell.Current.GoToAsync(route);
}
