using Microsoft.Maui.Dispatching;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>Attach flow duplicated from <see cref="ChatThreadView"/>'s code-behind rather than shared — see that class's own remarks on why native prompts live in page code-behind, not the ViewModel.</summary>
public partial class GroupChatPage : ContentPage
{
    private readonly GroupChatViewModel _viewModel;
    private readonly ISharedLibraryService _libraryService;

    public GroupChatPage(GroupChatViewModel viewModel, ISharedLibraryService libraryService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _viewModel.ScrollToBottomRequested += ScrollToLatest;
        _viewModel.ScrollAnchorRequested += ScrollToAnchor;
        MessagesView.Scrolled += OnMessagesScrolled;
    }

    private void ScrollToLatest()
    {
        // Always jump to the newest message (the user's ask), marshaled to the UI thread and guarded
        // — same reasoning as ChatThreadView.ScrollToLatest.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var messages = _viewModel.Messages;
            if (messages is null || messages.Count == 0) return;
            try { MessagesView.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false); }
            catch { /* layout not ready yet — harmless */ }
        });
    }

    /// <summary>Scroll-up history paging — only on a genuine upward scroll to the top, so it never fires during the initial layout/scroll-to-newest (see ChatThreadView.OnMessagesScrolled).</summary>
    private void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (e.VerticalDelta < 0 && e.FirstVisibleItemIndex <= 2 && _viewModel.HasOlderMessages && _viewModel.LoadOlderCommand.CanExecute(null))
            _viewModel.LoadOlderCommand.Execute(null);
    }

    private void ScrollToAnchor(GroupMessageItem anchor)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { MessagesView.ScrollTo(anchor, position: ScrollToPosition.Start, animate: false); }
            catch { /* layout not ready — harmless */ }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.StartListening();

        // Defer the data load until the push animation has finished (2026-09-11). Populating the
        // CollectionView is the heaviest UI-thread work on open; doing it while the page is still
        // sliding in left-to-right janked the slide mid-way (freeze + spinner + snap — the
        // unprofessional stutter the user reported). Showing the spinner up front lets the page slide
        // in smoothly on an unburdened UI thread, then the content renders once it has arrived.
        _viewModel.IsLoading = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () => _viewModel.LoadCommand.Execute(null));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopListening();
    }

    private async void OnAttachClicked(object? sender, EventArgs e)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync("Přiložit soubor", "Zrušit", null, "Vybrat z knihovny", "Nahrát nový soubor");
        if (choice is "Vybrat z knihovny")
        {
            await PickFromLibraryAsync();
        }
        else if (choice is "Nahrát nový soubor")
        {
            await UploadAndAttachAsync();
        }
    }

    private async Task PickFromLibraryAsync()
    {
        IReadOnlyList<SharedLibraryFileSummary> results;
        try
        {
            results = await _libraryService.SearchAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Nepodařilo se načíst knihovnu", ex.Message, "OK");
            return;
        }

        if (results.Count == 0)
        {
            await Shell.Current.DisplayAlertAsync("Sdílená knihovna je prázdná", "Nejprve nahrajte soubor, nebo místo toho použijte \"Nahrát nový soubor\".", "OK");
            return;
        }

        var labels = results.Select(r => string.IsNullOrWhiteSpace(r.FolderPath) ? r.FileName : $"{r.FolderPath}/{r.FileName}").ToArray();
        var choice = await Shell.Current.DisplayActionSheetAsync("Vyberte soubor", "Zrušit", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;

        var picked = results[index];
        _viewModel.SetPendingAttachment(picked.Id, picked.FileName);
    }

    private async Task UploadAndAttachAsync()
    {
        FileResult? picked;
        try
        {
            picked = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Vyberte soubor k přiložení" });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Nepodařilo se otevřít výběr souborů", ex.Message, "OK");
            return;
        }

        if (picked is null) return;

        try
        {
            await using var stream = await picked.OpenReadAsync();
            var summary = await _libraryService.UploadAsync(string.Empty, picked.FileName, [], stream);
            _viewModel.SetPendingAttachment(summary.Id, summary.FileName);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync($"Nepodařilo se nahrát '{picked.FileName}'", ex.Message, "OK");
        }
    }

    /// <summary>Confirmation dialog lives here per this codebase's established convention — <see cref="GroupChatViewModel.LeaveGroupCommand"/> does the actual removal once confirmed.</summary>
    private async void OnLeaveGroupClicked(object? sender, EventArgs e)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Opustit skupinu",
            $"Opravdu chcete opustit skupinu '{_viewModel.Title}'? Historie zpráv zůstane zachovaná, ale skupina zmizí z vašeho seznamu a ostatní členové uvidí, že jste odešli.",
            "Opustit",
            "Storno");
        if (!confirmed) return;

        await _viewModel.LeaveGroupCommand.ExecuteAsync(null);
    }
}
