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
            await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}", animate: false); // uniform with chat opens
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít '{item.FileName}': {ex.Message}";
        }
    }

    /// <summary>Tapped from the <see cref="AcuteStates"/> row — see that property's own remarks for the exact-tag-match-first, fall-back-to-search behavior.</summary>
    [RelayCommand]
    private async Task SearchAcuteStateAsync(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return;

        try
        {
            var tagged = await _libraryService.SearchAsync(tag: term);
            if (tagged.Count == 1)
            {
                var document = await _libraryService.DownloadAndImportAsync(tagged[0].Id);
                await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}", animate: false);
                return;
            }
        }
        catch
        {
            // Best-effort — fall through to the plain text-search shortcut below rather than
            // leaving the tap doing nothing (e.g. a transient network blip on the tag lookup).
        }

        SearchQuery = term;
        await SearchAsync();
    }

    /// <summary>
    /// 2026-10-01, the content-approval workflow: a Modifier uploads a Draft instead of publishing
    /// directly. Reuses the exact same private (listed:false) upload <see cref="UploadAsync"/> already
    /// does for the direct-publish path — the only difference is what happens AFTER the upload
    /// (<see cref="ILibraryReviewService.CreateDraftAsync"/> instead of nothing/immediate visibility).
    /// </summary>
    [RelayCommand]
    private async Task UploadDraftAsync()
    {
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Nahrávat koncepty do knihovny může jen Admin nebo Modifier.";
            return;
        }

        FileResult? picked;
        try
        {
            picked = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Vyberte soubor pro koncept" });
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít výběr souborů: {ex.Message}";
            return;
        }
        if (picked is null) return;

        var title = await (Shell.Current?.CurrentPage?.DisplayPromptAsync("Nový koncept", "Název dokumentu:", initialValue: System.IO.Path.GetFileNameWithoutExtension(picked.FileName)) ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(title)) return;

        IsUploading = true;
        StatusErrorMessage = null;
        try
        {
            var tags = UploadTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await using var stream = await picked.OpenReadAsync();
            var uploaded = await _libraryService.UploadAsync(UploadFolderPath, picked.FileName, tags, stream, listed: false);
            await _libraryReviewService.CreateDraftAsync(title.Trim(), UploadFolderPath, uploaded.Id, null);
            await SearchAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se vytvořit koncept '{picked.FileName}': {ex.Message}";
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private async Task SubmitDraftForReviewAsync(LibraryDocumentDraftItem? item)
    {
        if (item is null) return;

        StatusErrorMessage = null;
        try
        {
            await _libraryReviewService.SubmitForReviewAsync(item.Id);
            await RefreshReviewWorkflowStateAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se odeslat '{item.Title}' ke schválení: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenReviewQueueAsync() => await Shell.Current.GoToAsync(nameof(LibraryReviewQueuePage), animate: false);

    /// <summary>Tapped from the built-in tools list under "Nástroje" (2026-10-03) — see <see cref="NastrojeTools"/>'s own remarks.</summary>
    [RelayCommand]
    private async Task OpenGcsCalculatorAsync() => await Shell.Current.GoToAsync(nameof(GcsCalculatorPage), animate: false);

    /// <summary>Tapped from the built-in tools list under "Nástroje" (2026-10-03, second tool after GCS) — see <see cref="NastrojeTools"/>'s own remarks.</summary>
    [RelayCommand]
    private async Task OpenMurrayCalculatorAsync() => await Shell.Current.GoToAsync(nameof(MurrayScoreCalculatorPage), animate: false);

    /// <summary>Tapped from a sub-category card (2026-10-02) — see <see cref="Views.LibrarySubcategoryDetailPage"/>'s own remarks.</summary>
    [RelayCommand]
    private async Task OpenSubcategoryAsync(SubcategoryItem? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync(
            $"{nameof(LibrarySubcategoryDetailPage)}?subcategoryId={item.Id}&parentCategory={Uri.EscapeDataString(item.ParentCategory)}&name={Uri.EscapeDataString(item.Name)}",
            animate: false);
    }

}
