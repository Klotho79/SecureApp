using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Library;

/// <summary>
/// The library's top-level categories, one list for Knihovna, Redakce and the approval placement (2026-11-18 — it
/// was two hand-kept copies, and approving now needs it a third time).
/// </summary>
public static class LibraryStructure
{
    /// <summary>
    /// The five sections from the 2026-10-02 mockup, in their fixed order. A floor, not a ceiling: any other top
    /// folder a file was filed under shows up after them (<see cref="TopCategories"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> SeedCategories = ["Doporučení", "Resuscitace", "Postupy", "Výuka", "Nástroje"];

    /// <summary>Seed categories, then every other top-level folder in <paramref name="files"/>, alphabetically.</summary>
    public static List<string> TopCategories(IEnumerable<SharedLibraryFileSummary> files)
    {
        // Only the segment before the first '/' — a sub-category file's FolderPath is "{Top}/{Sub}".
        var extra = files
            .Select(f => f.FolderPath)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Split('/', 2)[0].Trim())
            .Where(f => f.Length > 0 && !SeedCategories.Contains(f, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        return SeedCategories.Concat(extra).ToList();
    }
}
