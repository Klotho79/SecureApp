using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>
/// The group message-thread + member card + compose UI, extracted out of <see cref="GroupChatPage"/>
/// (2026-09-13) so the same view can be hosted two ways: as a pushed page (<see cref="GroupChatPage"/>,
/// e.g. right after creating a group) and embedded persistently in <see cref="ChatListPage"/>'s phone
/// overlay — shown/hidden instead of a page rebuilt each open, so reopening a group is instant. Mirrors
/// <see cref="ChatThreadView"/> for 1:1. Does NOT manage its own Load/StartListening/StopListening
/// lifecycle — the host does that. Scroll subscriptions are (re)wired in OnBindingContextChanged so the
/// view can be reused for a different group.
/// </summary>
public partial class GroupChatThreadView : ContentView
{
    private readonly ISharedLibraryService _libraryService;

    private GroupChatViewModel? _subscribedViewModel;

    public GroupChatThreadView(ISharedLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        MessagesView.Scrolled += OnMessagesScrolled;
    }

    private GroupChatViewModel? ViewModel => BindingContext as GroupChatViewModel;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.ScrollToBottomRequested -= ScrollToLatest;
            _subscribedViewModel.ScrollAnchorRequested -= ScrollToAnchor;
        }

        _subscribedViewModel = ViewModel;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.ScrollToBottomRequested += ScrollToLatest;
            _subscribedViewModel.ScrollAnchorRequested += ScrollToAnchor;
        }
    }

    private void ScrollToLatest()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var messages = ViewModel?.Messages;
            if (messages is null || messages.Count == 0) return;
            try { MessagesView.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false); }
            catch { /* layout not ready yet — harmless */ }
        });
    }

    private void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (e.VerticalDelta < 0 && e.FirstVisibleItemIndex <= 2 && ViewModel is { HasOlderMessages: true } vm && vm.LoadOlderCommand.CanExecute(null))
            vm.LoadOlderCommand.Execute(null);
    }

    private void ScrollToAnchor(GroupMessageItem anchor)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { MessagesView.ScrollTo(anchor, position: ScrollToPosition.Start, animate: false); }
            catch { /* layout not ready — harmless */ }
        });
    }

    private async void OnAttachClicked(object? sender, EventArgs e)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync("Přiložit soubor", "Zrušit", null, "Vybrat z knihovny", "Nahrát nový soubor");
        if (choice is "Vybrat z knihovny")
            await PickFromLibraryAsync();
        else if (choice is "Nahrát nový soubor")
            await UploadAndAttachAsync();
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
        ViewModel?.SetPendingAttachment(picked.Id, picked.FileName);
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
            ViewModel?.SetPendingAttachment(summary.Id, summary.FileName);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync($"Nepodařilo se nahrát '{picked.FileName}'", ex.Message, "OK");
        }
    }

    /// <summary>Confirmation dialog lives here per this codebase's convention — <see cref="GroupChatViewModel.LeaveGroupCommand"/> does the actual removal once confirmed.</summary>
    private async void OnLeaveGroupClicked(object? sender, EventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Opustit skupinu",
            $"Opravdu chcete opustit skupinu '{vm.Title}'? Historie zpráv zůstane zachovaná, ale skupina zmizí z vašeho seznamu a ostatní členové uvidí, že jste odešli.",
            "Opustit",
            "Storno");
        if (!confirmed) return;

        await vm.LeaveGroupCommand.ExecuteAsync(null);
    }
}
