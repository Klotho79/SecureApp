using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The two <see cref="DocumentBrowserViewModel"/> commands that genuinely need MAUI types (<c>FilePicker</c>, <c>Shell</c>) — see the class-level remarks on the other partial for why these are split out.</summary>
public sealed partial class DocumentBrowserViewModel
{
    [RelayCommand]
    private async Task ImportDocumentsAsync()
    {
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Vaše role (Viewer) nemůže importovat dokumenty.";
            return;
        }

        IEnumerable<FileResult?>? picked;
        try
        {
            picked = await FilePicker.PickMultipleAsync(new PickOptions { PickerTitle = "Vyberte dokumenty k importu" });
        }
        catch (Exception ex)
        {
            // Some platforms throw instead of returning null/empty when the user cancels or
            // there's no picker activity available; treat it as "nothing picked" either way.
            StatusErrorMessage = $"Nepodařilo se otevřít výběr souborů: {ex.Message}";
            return;
        }

        var files = picked?.Where(f => f is not null).Cast<FileResult>().ToList();
        if (files is null || files.Count == 0)
            return;

        IsImporting = true;
        StatusErrorMessage = null;
        var failures = new List<string>();
        // The root of Dokumenty mirrors the relay; own imports land in "Moje" (2026-11-16, see LibraryMirrorSync).
        var target = _currentFolderId ?? await Library.LibraryMirrorSync.EnsurePersonalFolderAsync(_folderRepository);

        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                await _importService.ImportAsync(stream, file.FileName, target);
            }
            catch (Exception ex)
            {
                failures.Add($"{file.FileName}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
            StatusErrorMessage = $"Import se nezdařil u {failures.Count} z {files.Count} souborů:\n{string.Join("\n", failures)}";

        IsImporting = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenDocumentAsync(DocumentItem? document)
    {
        if (document is null) return;

        var documentId = document.Id;
        if (document.RemoteLibraryFileId is { } libraryFileId)
        {
            // First open of a library file (2026-11-18): fetch it into the root once; from then on it opens
            // straight from the device, offline too.
            if (IsDownloading) return;
            IsDownloading = true;
            DownloadingText = $"Stahuji „{document.Title}“…";
            StatusErrorMessage = null;
            var index = Documents.IndexOf(document);
            if (index >= 0) Documents[index] = document with { Detail = "⏳ stahuje se…" };
            try
            {
                documentId = (await _libraryService.DownloadAndImportAsync(libraryFileId)).Id;
            }
            catch (HttpRequestException)
            {
                StatusErrorMessage = $"„{document.Title}“ ještě není v tomto zařízení a teď se nedá stáhnout — zkontrolujte připojení.";
                return;
            }
            catch (Exception ex)
            {
                StatusErrorMessage = $"„{document.Title}“ se nepodařilo stáhnout: {ex.Message}";
                return;
            }
            finally
            {
                IsDownloading = false;
                if (index >= 0 && index < Documents.Count && Documents[index].RemoteLibraryFileId == libraryFileId)
                    Documents[index] = document; // back to "☁ … stáhne se při otevření" until the list reloads
            }
        }

        // animate:false (2026-09-13) — uniform with chat opens; drops the push slide that made opening a
        // photo/document jank the same way the chat did.
        await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={documentId}", animate: false);
    }
}
