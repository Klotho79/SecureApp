using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Views;

/// <summary>
/// The "attach a file" flow shared by the 1:1 and group chat threads (<see cref="ChatThreadView"/> /
/// <see cref="GroupChatThreadView"/>): pick an existing shared-library file or upload a new one, then
/// hand the resulting library file id + name to the caller. Dialogs live here, in the view layer, per
/// this codebase's convention (view models never show UI).
/// </summary>
internal static class ChatAttachmentPicker
{
    public static async Task ChooseAndAttachAsync(ISharedLibraryService libraryService, Action<Guid, string> setPendingAttachment)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync("Přiložit soubor", "Zrušit", null, "Vybrat z knihovny", "Nahrát nový soubor");
        if (choice is "Vybrat z knihovny")
            await PickFromLibraryAsync(libraryService, setPendingAttachment);
        else if (choice is "Nahrát nový soubor")
            await UploadAndAttachAsync(libraryService, setPendingAttachment);
    }

    private static async Task PickFromLibraryAsync(ISharedLibraryService libraryService, Action<Guid, string> setPendingAttachment)
    {
        IReadOnlyList<SharedLibraryFileSummary> results;
        try
        {
            results = await libraryService.SearchAsync();
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
        setPendingAttachment(picked.Id, picked.FileName);
    }

    private static async Task UploadAndAttachAsync(ISharedLibraryService libraryService, Action<Guid, string> setPendingAttachment)
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
            // listed: false (2026-09-14) — a chat attachment is private, not added to the community library browser.
            var summary = await libraryService.UploadAsync(string.Empty, picked.FileName, [], stream, listed: false);
            setPendingAttachment(summary.Id, summary.FileName);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync($"Nepodařilo se nahrát '{picked.FileName}'", ex.Message, "OK");
        }
    }
}
