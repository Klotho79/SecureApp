using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class InProgressDocumentsPage : ContentPage
{
    private readonly InProgressDocumentsViewModel _viewModel;

    public InProgressDocumentsPage(InProgressDocumentsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
