using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class HomeAiPage : ContentPage
{
    private readonly HomeAiViewModel _viewModel;

    public HomeAiPage(HomeAiViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ScrollRequested += OnScrollRequested;
        await _viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ScrollRequested -= OnScrollRequested;
    }

    private void OnScrollRequested()
    {
        if (_viewModel.Messages.Count == 0) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { MessagesView.ScrollTo(_viewModel.Messages.Count - 1, position: ScrollToPosition.End, animate: false); }
            catch { /* the list may be mid-update; the next request scrolls */ }
        });
    }
}
