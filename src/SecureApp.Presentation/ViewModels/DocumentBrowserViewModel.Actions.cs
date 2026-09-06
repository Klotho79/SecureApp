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

        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                await _importService.ImportAsync(stream, file.FileName, _currentFolderId);
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

        await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}");
    }
}
