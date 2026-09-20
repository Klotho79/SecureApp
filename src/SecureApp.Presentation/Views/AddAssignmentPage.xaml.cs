using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class AddAssignmentPage : ContentPage
{
    private readonly AddAssignmentViewModel _viewModel;

    public AddAssignmentPage(AddAssignmentViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.Saved += OnSaved;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private async void OnSaved() => await Shell.Current.GoToAsync("..");
}
