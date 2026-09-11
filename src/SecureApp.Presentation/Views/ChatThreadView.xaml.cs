using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>
/// The actual message-thread + compose-bar UI, extracted out of <see cref="ChatPage"/> (2026-09-06)
/// so the same view can be hosted two ways: as a full pushed page on narrow/phone widths (still
/// <see cref="ChatPage"/>, unchanged behavior) and embedded directly in <see cref="ChatListPage"/>'s
/// detail pane on wide/desktop widths — a real gap the user caught live: Windows' Shell back
/// button doesn't reliably bring you back from a pushed ChatPage to the list (works fine on
/// Android), and a persistent list+thread split sidesteps that entirely rather than chasing the
/// platform bug directly. Does not manage its own ViewModel's Load/StartListening/StopListening
/// lifecycle — whoever hosts this (ChatPage's OnAppearing/OnDisappearing, or ChatListPage's
/// selection-changed/page-lifecycle handling) is responsible for that, same as before.
/// </summary>
public partial class ChatThreadView : ContentView
{
    private readonly ISharedLibraryService _libraryService;

    private ChatViewModel? _subscribedViewModel;

    public ChatThreadView(ISharedLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
    }

    private ChatViewModel? ViewModel => BindingContext as ChatViewModel;

    /// <summary>
    /// The host (ChatPage, or ChatListPage's detail pane) sets our BindingContext to the ChatViewModel
    /// after construction — and may reuse this view for a different chat — so the scroll-to-latest
    /// subscription (2026-09-11) is (re)wired here rather than in the constructor. Unsubscribes from a
    /// previous VM first so switching chats never leaves a dangling handler firing on the wrong view.
    /// </summary>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_subscribedViewModel is not null)
            _subscribedViewModel.ScrollToBottomRequested -= ScrollToLatest;

        _subscribedViewModel = ViewModel;

        if (_subscribedViewModel is not null)
            _subscribedViewModel.ScrollToBottomRequested += ScrollToLatest;
    }

    private void ScrollToLatest()
    {
        // Always jump to the newest message (the user's ask). Marshaled to the UI thread since the
        // event can be raised from a background continuation, and guarded — an empty thread has
        // nothing to scroll to. Not animated: on open it should just be there, not visibly scroll.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var messages = ViewModel?.Messages;
            if (messages is null || messages.Count == 0) return;
            try { MessagesView.ScrollTo(messages[^1], position: ScrollToPosition.End, animate: false); }
            catch { /* a layout not ready yet is harmless — KeepLastItemInView still covers new items */ }
        });
    }

    /// <summary>
    /// Attach flow: either pick an already-uploaded file from the shared library, or pick a local
    /// file and upload it first — either way ends by calling <see cref="ChatViewModel.SetPendingAttachment"/>.
    /// Uses <c>DisplayActionSheet</c>/<c>FilePicker</c> here (not the ViewModel) per this codebase's
    /// established "native prompts live in the page code-behind" convention.
    /// </summary>
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
}
