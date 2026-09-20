using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class AddContactPage : ContentPage
{
    private readonly AddContactViewModel _viewModel;

    public AddContactPage(AddContactViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.Saved += OnSaved;
    }

    private async void OnSaved() => await Shell.Current.GoToAsync("..");
}
