using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LibraryManagePage : ContentPage
{
    private readonly LibraryViewModel _viewModel;
    private readonly LibraryReviewQueueViewModel _reviewQueue;

    public LibraryManagePage(LibraryViewModel viewModel, LibraryReviewQueueViewModel reviewQueue)
    {
        InitializeComponent();
        // Set before the page's own BindingContext so the review card never inherits LibraryViewModel.
        ReviewQueueSection.BindingContext = _reviewQueue = reviewQueue;
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await _viewModel.RefreshReviewWorkflowStateAsync();
        if (_viewModel.IsDocumentReviewer)
            await _reviewQueue.LoadCommand.ExecuteAsync(null);
    }
}
