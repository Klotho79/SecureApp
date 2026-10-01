using System.ComponentModel;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;
    private System.Diagnostics.Stopwatch? _ttiStopwatch;

    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // TTI telemetry (2026-10-01, see LibraryViewModel.SearchAsync's own remarks on the AppLog
        // reuse) — started here, logged the first time IsLoading flips back to false below.
        _ttiStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SearchCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryViewModel.IsLoading) || _viewModel.IsLoading || _ttiStopwatch is null) return;

        Infrastructure.AppLog.Metric("tti.library_page", _ttiStopwatch.Elapsed.TotalMilliseconds);
        _ttiStopwatch = null; // only the first load counts as TTI, not every subsequent re-search
    }

    private void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        ResultsView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is LibraryFileItem item)
            _viewModel.OpenFileCommand.Execute(item);
    }
}
