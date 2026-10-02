using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Library sub-category management (2026-10-02) — moved out of the Library browse page per the
/// user's own "sprava a pridavani kategorii ma byt v nastaveni" ask, consolidating it alongside the
/// rest of library management the same way upload/review already live behind "✏ Spravovat" rather
/// than on the browse page itself. Browsing/opening a sub-category still lives on
/// <see cref="LibraryViewModel"/>'s own page; only add/delete moved here. Split into its own partial
/// the same way Updates/Community are.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>Same floor-not-ceiling seed list as <see cref="LibraryViewModel"/>'s own — duplicated
    /// on purpose, two small independently-readable lists beat reaching a shared static across
    /// ViewModels for five strings.</summary>
    private static readonly string[] LibrarySeedCategories = ["Doporučení", "Resuscitace", "Postupy", "Výuka", "Nástroje"];

    [ObservableProperty]
    public partial ObservableCollection<string> LibraryTopCategories { get; set; } = [];

    [ObservableProperty]
    public partial string? SelectedLibraryTopCategory { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SubcategoryItem> LibrarySubcategories { get; set; } = [];

    [ObservableProperty]
    public partial bool HasLibrarySubcategories { get; set; }

    /// <summary>Mirrors <see cref="HasLibrarySubcategories"/> — same established pattern this app
    /// uses throughout (see e.g. <c>HasNoSharedLibraryKey</c>) instead of an IValueConverter.</summary>
    [ObservableProperty]
    public partial bool HasNoLibrarySubcategories { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingLibrarySubcategories { get; set; }

    [ObservableProperty]
    public partial string NewLibrarySubcategoryNameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? LibraryCategoriesStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasLibraryCategoriesStatus { get; set; }

    partial void OnLibraryCategoriesStatusTextChanged(string? value) => HasLibraryCategoriesStatus = !string.IsNullOrEmpty(value);

    partial void OnHasLibrarySubcategoriesChanged(bool value) => HasNoLibrarySubcategories = !value;

    partial void OnSelectedLibraryTopCategoryChanged(string? value) => _ = RefreshLibrarySubcategoriesAsync();

    /// <summary>Best-effort, same posture as the rest of this page's background extras (e.g.
    /// <see cref="RefreshCanPostToBoard"/>) — a failure here must never block Settings from loading.
    /// Only called for a Modifier/Admin (<see cref="CanManageLibrary"/>); a Viewer has nothing to
    /// manage here.</summary>
    private async Task RefreshLibraryCategoriesAsync()
    {
        if (!CanManageLibrary) return;
        try
        {
            var all = await _sharedLibraryService.SearchAsync();
            // Only the segment before the first '/' — a sub-category file's FolderPath is
            // "{Top}/{Sub}", matching LibraryViewModel.RefreshCategoriesAsync's own split.
            var topLevelFolders = all
                .Select(f => f.FolderPath)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f!.Split('/', 2)[0]);

            var names = LibrarySeedCategories
                .Concat(topLevelFolders)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            LibraryTopCategories = new ObservableCollection<string>(names);
            SelectedLibraryTopCategory ??= names.FirstOrDefault();
            await RefreshLibrarySubcategoriesAsync();
        }
        catch (Exception ex)
        {
            _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Warning, "Nepodařilo se načíst kategorie knihovny.", nameof(SettingsViewModel), ex);
        }
    }

    private async Task RefreshLibrarySubcategoriesAsync()
    {
        if (SelectedLibraryTopCategory is null)
        {
            LibrarySubcategories = [];
            HasLibrarySubcategories = false;
            return;
        }

        IsLoadingLibrarySubcategories = true;
        try
        {
            var myDeviceId = (await _transportSettingsRepository.GetAsync())?.AssignedDeviceId;
            var items = await _sharedLibraryService.ListSubcategoriesAsync(SelectedLibraryTopCategory);
            LibrarySubcategories = new ObservableCollection<SubcategoryItem>(items.Select(s => new SubcategoryItem(
                s.Id, s.ParentCategory, s.Name,
                myDeviceId is { } id && string.Equals(s.CreatedByDeviceId, id.ToString(), StringComparison.OrdinalIgnoreCase))));
            HasLibrarySubcategories = LibrarySubcategories.Count > 0;
        }
        catch (Exception ex)
        {
            LibraryCategoriesStatusText = $"Nepodařilo se načíst podkategorie: {ex.Message}";
        }
        finally
        {
            IsLoadingLibrarySubcategories = false;
        }
    }

    [RelayCommand]
    private void SelectLibraryTopCategory(string? category) => SelectedLibraryTopCategory = category;

    [RelayCommand]
    private async Task AddLibrarySubcategoryAsync()
    {
        if (SelectedLibraryTopCategory is null)
        {
            LibraryCategoriesStatusText = "Nejprve vyberte kategorii.";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewLibrarySubcategoryNameText))
        {
            LibraryCategoriesStatusText = "Zadejte název podkategorie.";
            return;
        }

        LibraryCategoriesStatusText = null;
        try
        {
            await _sharedLibraryService.CreateSubcategoryAsync(SelectedLibraryTopCategory, NewLibrarySubcategoryNameText.Trim());
            NewLibrarySubcategoryNameText = string.Empty;
            await RefreshLibrarySubcategoriesAsync();
        }
        catch (Exception ex)
        {
            LibraryCategoriesStatusText = $"Nepodařilo se vytvořit podkategorii: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteLibrarySubcategoryAsync(SubcategoryItem? item)
    {
        if (item is null) return;
        try
        {
            await _sharedLibraryService.DeleteSubcategoryAsync(item.Id);
            LibrarySubcategories = new ObservableCollection<SubcategoryItem>(LibrarySubcategories.Where(s => s.Id != item.Id));
            HasLibrarySubcategories = LibrarySubcategories.Count > 0;
        }
        catch (Exception ex)
        {
            LibraryCategoriesStatusText = $"'{item.Name}' se nepodařilo smazat: {ex.Message}";
        }
    }
}
