using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LogbookChecklistPage : ContentPage
{
    private readonly LogbookChecklistViewModel _viewModel;

    public LogbookChecklistPage(LogbookChecklistViewModel viewModel)
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
