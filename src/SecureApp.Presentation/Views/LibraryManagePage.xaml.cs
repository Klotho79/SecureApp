using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LibraryManagePage : ContentPage
{
    private readonly LibraryViewModel _viewModel;

    public LibraryManagePage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.RefreshReviewWorkflowStateAsync();
    }
}
