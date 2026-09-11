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
            catch { /* layout not ready yet — KeepLastItemInView still covers new items */ }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
        _viewModel.StartListening();
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
