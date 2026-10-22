using System.Diagnostics;
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
        => await Shell.Current.GoToAsync($"{nameof(NotificationDetailPage)}?notificationId={notificationId}");

    private async void OnSearchClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SmartSearchPage));

    private async void OnSettingsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//SettingsTab");

    private async void OnWorkplaceClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(WorkplacePage));

    private async void OnAcuteStatesClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//LibraryTab");

    private async void OnContactsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//ContactsTab");

    private async void OnRoomStatusClicked(object? sender, EventArgs e)
    {
        _viewModel.CloseRoomStatusPickerCommand.Execute(null);
        await Shell.Current.GoToAsync(nameof(RoomStatusPage));
    }

    private async void OnOpicentrumBoardClicked(object? sender, EventArgs e)
    {
        // 2026-10-22 profiling (user's own ask: "změř od kliknutí na otevřít po zobrazení") —
        // starts before Shell.GoToAsync so OpicentrumBoardPage's own TTI stopwatch can pick this
        // up and cover navigation + page/VM construction too, not just its own OnAppearing-onward work.
        OpicentrumBoardPage.ClickStopwatch = Stopwatch.StartNew();
        await Shell.Current.GoToAsync(nameof(OpicentrumBoardPage));
    }

    private async void OnRequestNavigate(string route) => await Shell.Current.GoToAsync(route);
}
