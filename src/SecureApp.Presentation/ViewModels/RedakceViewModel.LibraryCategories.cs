using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Library sub-category management — moved here from SettingsViewModel.LibraryCategories.cs
/// (2026-10-13, user's own ask: "V nastaveni sytem kategorie knihovny patri taky do redakce"), same
/// consolidation as the rest of <see cref="RedakceViewModel"/>'s own cards. Only the "Kategorie
/// knihovny" card (add/delete sub-categories + the "✏ Spravovat obsah" link) moved; the plain "Klíč
/// sdílené knihovny" status card on Nastavení→Systém→Knihovna is informational, not an edit action,
/// and stayed there.
/// </summary>
public sealed partial class RedakceViewModel
{
    /// <summary>Same floor-not-ceiling seed list as <see cref="LibraryViewModel"/>'s own — duplicated
    /// on purpose, two small independently-readable lists beat reaching a shared static across
    /// ViewModels for five strings.</summary>
    private static readonly string[] LibrarySeedCategories = ["Doporučení", "Resuscitace", "Postupy", "Výuka", "Nástroje"];

    [ObservableProperty]
    public partial ObservableCollection<string> LibraryTopCategories { get; set; }

    [ObservableProperty]
    public partial string? SelectedLibraryTopCategory { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SubcategoryItem> LibrarySubcategories { get; set; }

    [ObservableProperty]
    public partial bool HasLibrarySubcategories { get; set; }

    /// <summary>Mirrors <see cref="HasLibrarySubcategories"/> — same established pattern this app
    /// uses throughout instead of an IValueConverter.</summary>
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

    /// <summary>Best-effort, same posture as the rest of this page's background extras — a failure
    /// here must never block Redakce from loading. Only called for a Modifier/Admin
    /// (<see cref="CanManageLibrary"/>); a Viewer has nothing to manage here.</summary>
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

            // Seed categories keep their fixed mockup order, same as LibraryViewModel's own —
            // only a genuinely new, community-typed folder name falls back to alphabetical.
            var extraFolders = topLevelFolders
                .Where(f => !LibrarySeedCategories.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            var names = LibrarySeedCategories.Concat(extraFolders).ToList();

            LibraryTopCategories = new ObservableCollection<string>(names);
            // Assigning a first selection already triggers OnSelectedLibraryTopCategoryChanged →
            // RefreshLibrarySubcategoriesAsync; only refresh explicitly when that handler won't fire,
            // otherwise the first Redakce open fetched the sub-categories twice.
            var hadSelection = SelectedLibraryTopCategory is not null;
            SelectedLibraryTopCategory ??= names.FirstOrDefault();
            if (hadSelection || SelectedLibraryTopCategory is null)
                await RefreshLibrarySubcategoriesAsync();
        }
        catch (Exception ex)
        {
            _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Warning, "Nepodařilo se načíst kategorie knihovny.", nameof(RedakceViewModel), ex);
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

    /// <summary>The "✏ Spravovat" entry (upload/"Moje koncepty"/review queue) that used to sit on the
    /// Library browse page itself, then on Nastavení→Systém→Knihovna — moved here, same consolidation
    /// as sub-category add/delete right below.</summary>
    [RelayCommand]
    private void OpenLibraryManage() => RequestNavigate?.Invoke(nameof(Views.LibraryManagePage));

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
