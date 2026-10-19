using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class ChangelogPage : ContentPage
{
    private readonly ChangelogViewModel _viewModel;

    public ChangelogPage(ChangelogViewModel viewModel)
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
