using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class NewGroupPage : ContentPage
{
    private readonly NewGroupViewModel _viewModel;

    public NewGroupPage(NewGroupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadMembersCommand.Execute(null);
    }
}
