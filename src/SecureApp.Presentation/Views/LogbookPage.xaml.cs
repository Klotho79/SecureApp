using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LogbookPage : ContentPage
{
    private readonly LogbookViewModel _viewModel;

    public LogbookPage(LogbookViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private void OnChecklistSelected(object? sender, SelectionChangedEventArgs e)
    {
        ChecklistsView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is LogbookChecklistListItem item)
            _viewModel.OpenChecklistCommand.Execute(item);
    }
}
