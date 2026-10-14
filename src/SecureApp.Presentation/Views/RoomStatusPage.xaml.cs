using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class RoomStatusPage : ContentPage
{
    private readonly RoomStatusViewModel _viewModel;

    public RoomStatusPage(RoomStatusViewModel viewModel)
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
