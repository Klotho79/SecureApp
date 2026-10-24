using System.ComponentModel;
using System.Diagnostics;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class WorkplacePage : ContentPage
{
    private readonly WorkplaceViewModel _viewModel;

    public WorkplacePage(WorkplaceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RequestOpenDay += OnRequestOpenDay;
        _viewModel.RequestOpenRoomStatus += OnRequestOpenRoomStatus;
    }

    private Stopwatch? _ttiStopwatch;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // TTI telemetry (2026-10-24), same pattern as OpicentrumBoardPage: OnAppearing to the first local
        // render finishing, and to the layout pass after it — readable via /admin/applog/{deviceId}.
        _ttiStopwatch = Stopwatch.StartNew();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkplaceViewModel.IsLoading) || _viewModel.IsLoading || _ttiStopwatch is not { } sw) return;

        _ttiStopwatch = null; // only the first render after appearing counts
        AppLog.Metric("tti.workplace_page", sw.Elapsed.TotalMilliseconds);
        Dispatcher.Dispatch(() => AppLog.Metric("tti.workplace_page.post_layout", sw.Elapsed.TotalMilliseconds));
    }

    private async void OnRequestOpenDay(DateOnly date, Guid? assignmentId)
    {
        var dateText = date.ToString("yyyy-MM-dd");
        var route = assignmentId is { } id
            ? $"{nameof(AddAssignmentPage)}?date={dateText}&assignmentId={id}"
            : $"{nameof(AddAssignmentPage)}?date={dateText}";
        await Infrastructure.ShellNavigation.GoToAsync(route);
    }

    private async void OnRequestOpenRoomStatus() => await Infrastructure.ShellNavigation.GoToAsync(nameof(RoomStatusPage));
}
