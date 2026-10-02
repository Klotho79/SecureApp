namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One sub-category under a top-level Library tab (2026-10-02) — e.g. "Severe Hemorrhage Protocol"
/// under "Doporučení". Groups files (via <c>SharedLibraryFileSummary.FolderPath</c> = "{ParentCategory}/{Name}")
/// and <see cref="LibraryLinkSummary"/> links. Admin/Modifier-managed via <c>ISharedLibraryService</c>.
/// </summary>
public sealed record LibrarySubcategorySummary(
    Guid Id,
    string ParentCategory,
    string Name,
    string CreatedByDeviceId,
    DateTimeOffset CreatedAtUtc);
