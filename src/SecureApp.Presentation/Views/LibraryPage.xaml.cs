using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;

    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.SearchCommand.Execute(null);
    }

    private void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        ResultsView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is LibraryFileItem item)
            _viewModel.OpenFileCommand.Execute(item);
    }
}
