using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LogbookManagePage : ContentPage
{
    private readonly LogbookManageViewModel _viewModel;

    public LogbookManagePage(LogbookManageViewModel viewModel)
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
