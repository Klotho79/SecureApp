using System.ComponentModel;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LibraryReviewQueuePage : ContentPage
{
    private readonly LibraryReviewQueueViewModel _viewModel;
    private System.Diagnostics.Stopwatch? _ttiStopwatch;

    public LibraryReviewQueuePage(LibraryReviewQueueViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // TTI telemetry (2026-10-01) — see LibraryPage.xaml.cs's own remarks, same pattern.
        _ttiStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
        if (e.PropertyName != nameof(LibraryReviewQueueViewModel.IsLoading) || _viewModel.IsLoading || _ttiStopwatch is null) return;

        Infrastructure.AppLog.Metric("tti.library_review_queue_page", _ttiStopwatch.Elapsed.TotalMilliseconds);
        _ttiStopwatch = null;
    }
}
