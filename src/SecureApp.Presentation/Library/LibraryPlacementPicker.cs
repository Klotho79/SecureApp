using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Library;

/// <summary>
/// "Where in the library does this go?" — category, then level by level a branch or "directly here", for as long as
/// there are branches (Výuka › Anestezie › Regionální). Shared by approving a document (placing it is required,
/// 2026-11-18) and moving a teaching material.
/// </summary>
public static class LibraryPlacementPicker
{
    /// <summary>Category + up to three branch levels — the relay accepts the same.</summary>
    public const int MaxDepth = 4;

    private const string Cancel = "Zrušit";

    /// <summary>The chosen folder path ("Výuka/Anestezie/Regionální"), or null when the user backs out.</summary>
    public static async Task<string?> PickAsync(Page page, ISharedLibraryService library, string title, string? currentFolder)
    {
        var categories = LibraryStructure.TopCategories(await library.SearchAsync());
        var now = string.IsNullOrWhiteSpace(currentFolder) ? "" : $" (teď: {Display(currentFolder)})";
        var path = await page.DisplayActionSheetAsync($"{title}{now} — kategorie", Cancel, null, categories.ToArray());
        if (path is null or Cancel) return null;

        for (var depth = 1; depth < MaxDepth; depth++)
        {
            var branches = (await library.ListSubcategoriesAsync(path))
                .Select(s => s.Name)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (branches.Count == 0) return path;

            var direct = $"Přímo do „{path[(path.LastIndexOf('/') + 1)..]}“";
            var choice = await page.DisplayActionSheetAsync($"{Display(path)} — podkategorie", Cancel, null, [.. branches, direct]);
            if (choice is null or Cancel) return null;
            if (choice == direct) return path;
            path = $"{path}/{choice}";
        }
        return path;
    }

    public static string Display(string folderPath) => folderPath.Replace("/", " › ");
}
