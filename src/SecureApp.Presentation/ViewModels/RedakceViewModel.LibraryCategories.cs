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

    /// <summary>
    /// Whose sub-categories are listed/added: the picked top category, or one of its sub-categories after "›" — a third
    /// level such as Výuka › Anestezie › Regionální (2026-11-18). Same relay table, the parent is just this path.
    /// </summary>
    [ObservableProperty]
    public partial string? LibraryParentPath { get; set; }

    [ObservableProperty]
    public partial bool IsInLibrarySubcategory { get; set; }

    [ObservableProperty]
    public partial string LibraryParentPathText { get; set; } = string.Empty;


    partial void OnSelectedLibraryTopCategoryChanged(string? value) => LibraryParentPath = value;

    partial void OnLibraryParentPathChanged(string? value)
    {
        IsInLibrarySubcategory = value?.Contains('/') == true;
        LibraryParentPathText = value?.Replace("/", " › ") ?? string.Empty;
        _ = RefreshLibrarySubcategoriesAsync();
    }

    [RelayCommand]
    private void OpenLibrarySubcategory(SubcategoryItem? item)
    {
        if (item is null) return;
        var path = $"{item.ParentCategory}/{item.Name}";
        if (path.Split('/').Length >= Library.LibraryPlacementPicker.MaxDepth)
        {
            LibraryCategoriesStatusText = "Hlouběji už podkategorie nejdou (nejvýš kategorie a tři úrovně).";
            return;
        }
        LibraryCategoriesStatusText = null;
        LibraryParentPath = path;
    }

    [RelayCommand]
    private void LibraryParentUp()
    {
        if (LibraryParentPath is not { } path || !path.Contains('/')) return;
        LibraryCategoriesStatusText = null;
        LibraryParentPath = path[..path.LastIndexOf('/')];
    }

    /// <summary>Best-effort, same posture as the rest of this page's background extras — a failure
    /// here must never block Redakce from loading. Only called for a Modifier/Admin
    /// (<see cref="CanManageLibrary"/>); a Viewer has nothing to manage here.</summary>
    private async Task RefreshLibraryCategoriesAsync()
    {
        if (!CanManageLibraryStructure) return;
        try
        {
            var all = await _sharedLibraryService.SearchAsync();
            // Seed categories in their fixed order, then any other top folder (the same list Knihovna shows).
            var names = Library.LibraryStructure.TopCategories(all);

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
        if (LibraryParentPath is not { } parent)
        {
            LibrarySubcategories = [];
            HasLibrarySubcategories = false;
            return;
        }

        IsLoadingLibrarySubcategories = true;
        try
        {
            var myDeviceId = (await _transportSettingsRepository.GetAsync())?.AssignedDeviceId;
            var items = await _sharedLibraryService.ListSubcategoriesAsync(parent);
            if (parent != LibraryParentPath) return; // moved elsewhere meanwhile
            // Admin/Modifier/Redaktor delete any (empty) branch, not only their own (2026-11-18; the relay checks both).
            LibrarySubcategories = new ObservableCollection<SubcategoryItem>(items.Select(s => new SubcategoryItem(
                s.Id, s.ParentCategory, s.Name,
                CanManageLibraryStructure || myDeviceId is { } id && string.Equals(s.CreatedByDeviceId, id.ToString(), StringComparison.OrdinalIgnoreCase))));
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
    private async Task AddLibrarySubcategoryAsync()
    {
        if (LibraryParentPath is not { } parent)
        {
            LibraryCategoriesStatusText = "Nejprve vyberte kategorii.";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewLibrarySubcategoryNameText))
        {
            LibraryCategoriesStatusText = "Zadejte název podkategorie.";
            return;
        }
        if (NewLibrarySubcategoryNameText.Contains('/'))
        {
            LibraryCategoriesStatusText = "Název podkategorie nesmí obsahovat lomítko „/“.";
            return;
        }

        LibraryCategoriesStatusText = null;
        try
        {
            await _sharedLibraryService.CreateSubcategoryAsync(parent, NewLibrarySubcategoryNameText.Trim());
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
