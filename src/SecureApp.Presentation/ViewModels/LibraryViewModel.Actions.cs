using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The <see cref="LibraryViewModel"/> commands that need MAUI types (<c>FilePicker</c>, <c>Shell</c>) — see the class-level remarks on the other partial for why these are split out.</summary>
public sealed partial class LibraryViewModel
{
    [RelayCommand]
    private async Task UploadAsync()
    {
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Přidávat do sdílené knihovny může jen Admin nebo Modifier.";
            return;
        }

        FileResult? picked;
        try
        {
            picked = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Vyberte soubor ke sdílení" });
        }
        catch (Exception ex)
        {
            // Some platforms throw instead of returning null when the user cancels or there's no picker activity available.
            StatusErrorMessage = $"Nepodařilo se otevřít výběr souborů: {ex.Message}";
            return;
        }

        if (picked is null) return;

        IsUploading = true;
        StatusErrorMessage = null;
        try
        {
            var tags = UploadTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await using var stream = await picked.OpenReadAsync();
            await _libraryService.UploadAsync(UploadFolderPath, picked.FileName, tags, stream);
            await SearchAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se nahrát '{picked.FileName}': {ex.Message}";
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync(LibraryFileItem? item)
    {
        if (item is null) return;

        StatusErrorMessage = null;
        try
        {
            var document = await _libraryService.DownloadAndImportAsync(item.Id);
            await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}");
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít '{item.FileName}': {ex.Message}";
        }
    }
}
