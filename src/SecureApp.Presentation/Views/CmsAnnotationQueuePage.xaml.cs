using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class CmsAnnotationQueuePage : ContentPage
{
    private readonly CmsAnnotationQueueViewModel _viewModel;

    public CmsAnnotationQueuePage(CmsAnnotationQueueViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var count = Queue.SelectedItems?.Count ?? 0;
        KeepSelectedButton.IsEnabled = count > 0;
        DeclineSelectedButton.IsEnabled = count > 0;
    }

    private async void OnKeepSelectedClicked(object? sender, EventArgs e) => await ModerateSelectedAsync("kept", "Ponechat vybrané poznámky jako komentáře viditelné všem?");

    private async void OnDeclineSelectedClicked(object? sender, EventArgs e) => await ModerateSelectedAsync("declined", "Zamítnout vybrané návrhy?");

    private async Task ModerateSelectedAsync(string decision, string question)
    {
        var items = Queue.SelectedItems?.Cast<CmsAnnotationQueueItem>().ToList() ?? [];
        if (items.Count == 0) return;
        if (!await DisplayAlertAsync("Hromadná akce", $"{question} ({items.Count}×)", "Ano", "Zrušit")) return;

        await _viewModel.ModerateManyAsync(items, decision);
        Queue.SelectedItems?.Clear();
        KeepSelectedButton.IsEnabled = false;
        DeclineSelectedButton.IsEnabled = false;
    }
}
