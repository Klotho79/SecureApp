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
        // Coming back from Android's battery setting resumes the window without re-firing OnAppearing.
        if (Window is { } window) window.Resumed += OnWindowResumed;
    }

    private void OnWindowResumed(object? sender, EventArgs e) => _viewModel.RefreshBackgroundRunStatus();

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Window is { } window) window.Resumed -= OnWindowResumed;
        _viewModel.StopObservingConnection();
        _viewModel.StopActivationPolling();
        _viewModel.ClearMemberManagement();
    }
}
