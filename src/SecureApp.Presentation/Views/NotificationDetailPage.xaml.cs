using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class NotificationDetailPage : ContentPage
{
    private readonly NotificationDetailViewModel _viewModel;

    public NotificationDetailPage(NotificationDetailViewModel viewModel)
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
