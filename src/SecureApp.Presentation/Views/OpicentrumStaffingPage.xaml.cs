using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class OpicentrumStaffingPage : ContentPage
{
    private readonly OpicentrumStaffingViewModel _viewModel;

    public OpicentrumStaffingPage(OpicentrumStaffingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Attach();
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Detach();
    }
}
