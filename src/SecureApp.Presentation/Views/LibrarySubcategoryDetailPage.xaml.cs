using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LibrarySubcategoryDetailPage : ContentPage
{
    private readonly LibrarySubcategoryDetailViewModel _viewModel;

    public LibrarySubcategoryDetailPage(LibrarySubcategoryDetailViewModel viewModel)
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
