using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
        _viewModel.StartObservingConnection();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopObservingConnection();
        _viewModel.StopActivationPolling();
        _viewModel.ClearMemberManagement();
    }
}
