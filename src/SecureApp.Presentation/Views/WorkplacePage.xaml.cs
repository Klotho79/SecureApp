using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class WorkplacePage : ContentPage
{
    private readonly WorkplaceViewModel _viewModel;

    public WorkplacePage(WorkplaceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RequestOpenDay += OnRequestOpenDay;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private async void OnRequestOpenDay(DateOnly date, Guid? assignmentId)
    {
        var dateText = date.ToString("yyyy-MM-dd");
        var route = assignmentId is { } id
            ? $"{nameof(AddAssignmentPage)}?date={dateText}&assignmentId={id}"
            : $"{nameof(AddAssignmentPage)}?date={dateText}";
        await Shell.Current.GoToAsync(route);
    }
}
