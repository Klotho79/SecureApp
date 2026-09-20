using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class NotificationsPage : ContentPage
{
    private readonly NotificationsViewModel _viewModel;

    public NotificationsPage(NotificationsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RequestOpenDetail += OnRequestOpenDetail;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private async void OnRequestOpenDetail(Guid notificationId)
        => await Shell.Current.GoToAsync($"{nameof(NotificationDetailPage)}?notificationId={notificationId}");

    private async void OnSearchClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SmartSearchPage));
}
