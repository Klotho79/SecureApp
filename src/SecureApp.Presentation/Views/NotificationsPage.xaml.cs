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
        _viewModel.RequestNavigate += OnRequestNavigate;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private async void OnRequestOpenDetail(Guid notificationId)
        => await Infrastructure.ShellNavigation.GoToAsync($"{nameof(NotificationDetailPage)}?notificationId={notificationId}");

    private async void OnSearchClicked(object? sender, EventArgs e)
        => await Infrastructure.ShellNavigation.GoToAsync(nameof(SmartSearchPage));

    private async void OnSettingsClicked(object? sender, EventArgs e)
        => await Infrastructure.ShellNavigation.GoToAsync("//SettingsTab");

    private async void OnWorkplaceClicked(object? sender, EventArgs e)
        => await Infrastructure.ShellNavigation.GoToAsync(nameof(WorkplacePage));

    private async void OnAcuteStatesClicked(object? sender, EventArgs e)
        => await Infrastructure.ShellNavigation.GoToAsync("//LibraryTab");

    private async void OnContactsClicked(object? sender, EventArgs e)
        => await Infrastructure.ShellNavigation.GoToAsync("//ContactsTab");

    private async void OnRoomStatusClicked(object? sender, EventArgs e)
    {
        _viewModel.CloseRoomStatusPickerCommand.Execute(null);
        await Infrastructure.ShellNavigation.GoToAsync(nameof(RoomStatusPage));
    }

    private async void OnOpicentrumBoardClicked(object? sender, EventArgs e)
        => await Infrastructure.ShellNavigation.GoToAsync(nameof(OpicentrumBoardPage));

    private async void OnRequestNavigate(string route) => await Infrastructure.ShellNavigation.GoToAsync(route);
}
