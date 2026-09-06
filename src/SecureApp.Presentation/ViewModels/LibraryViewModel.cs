using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Browses/searches the community's shared file library hosted on the relay (metadata only —
/// file name, folder, tags; the relay never sees content, so it cannot search inside it). Split
/// like <see cref="DocumentBrowserViewModel"/>: this partial is free of any MAUI type (only
/// Domain interfaces + CommunityToolkit.Mvvm); <c>LibraryViewModel.Actions.cs</c> holds the
/// FilePicker-based upload and Shell navigation to the document viewer.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ISharedLibraryService _libraryService;
    private readonly ICurrentUserService _currentUserService;

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial string FolderFilter { get; set; }

    [ObservableProperty]
    public partial string TagFilter { get; set; }

    [ObservableProperty]
    public partial string UploadFolderPath { get; set; }

    [ObservableProperty]
    public partial string UploadTags { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<LibraryFileItem> Results { get; set; }

    /// <summary>
    /// Category chips (2026-09-06) — deliberately NOT a fixed/hardcoded list like the redesign
    /// mockup's static "Emergency/Surgery/Pediatrics/…" row: every real community using this app
    /// picks its own categories by typing a folder name on upload (already-existing, unchanged
    /// behavior), so the chip row is derived from whatever folder names actually exist in the
    /// library right now, refreshed alongside every search. "All" clears the filter.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<LibraryCategoryChip> Categories { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsUploading { get; set; }

    [ObservableProperty]
    public partial bool CanUpload { get; set; }

    /// <summary>
    /// Gates the whole "Add a procedure" card (upload) — a real gap the user caught live
    /// (2026-09-06): unlike Documents' own Create/Rename/Delete/Import (already RBAC-gated per
    /// DEVELOPMENT_PLAN.md's Milestone 3 note), the shared community Library had no role check at
    /// all, so a Viewer could add/reorganize the whole team's shared procedure set. Same
    /// RoleAccessPolicy this app already uses everywhere else, not a new mechanism.
    /// </summary>
    [ObservableProperty]
    public partial bool CanModifyContent { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    public LibraryViewModel(ISharedLibraryService libraryService, ICurrentUserService currentUserService)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

        SearchQuery = string.Empty;
        FolderFilter = string.Empty;
        TagFilter = string.Empty;
        UploadFolderPath = string.Empty;
        UploadTags = string.Empty;
        Results = [];
        Categories = [];
        CanModifyContent = true;
        RecomputeCanUpload();
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    partial void OnIsUploadingChanged(bool value) => RecomputeCanUpload();

    partial void OnCanModifyContentChanged(bool value) => RecomputeCanUpload();

    private void RecomputeCanUpload() => CanUpload = !IsUploading && CanModifyContent;

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        CanModifyContent = RoleAccessPolicy.IsAllowed(_currentUserService.Current.Role, RbacAction.UploadLibraryFile);
        try
        {
            var results = await _libraryService.SearchAsync(
                string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
                string.IsNullOrWhiteSpace(FolderFilter) ? null : FolderFilter,
                string.IsNullOrWhiteSpace(TagFilter) ? null : TagFilter);

            Results = new ObservableCollection<LibraryFileItem>(results.Select(ToItem));
            IsEmpty = Results.Count == 0;

            await RefreshCategoriesAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not search the library: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// A starting structure so the chip row isn't empty before anyone has uploaded anything — the
    /// user's own field (anesthesiology/intensive care) plus a general announcements bucket. Not
    /// exclusive: any other folder name typed on upload shows up as its own chip too (see
    /// RefreshCategoriesAsync below), this is just a floor, not a ceiling. Worth making
    /// admin-editable later rather than a hardcoded list, if the community's categories evolve.
    /// </summary>
    private static readonly string[] SeedCategories = ["Anesthesiology", "Intensive Care Medicine", "Announcements"];

    /// <summary>
    /// Unfiltered fetch, deliberately separate from the (possibly filtered) Results above — the
    /// chip row needs to keep showing every category that exists regardless of which one is
    /// currently selected, not just whichever one the active filter happens to match.
    /// </summary>
    private async Task RefreshCategoriesAsync()
    {
        var all = await _libraryService.SearchAsync();
        var uploadedFolders = all
            .Select(f => f.FolderPath)
            .Where(f => !string.IsNullOrWhiteSpace(f));

        var distinctFolders = SeedCategories
            .Concat(uploadedFolders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        var chips = new List<LibraryCategoryChip> { new("All", string.IsNullOrEmpty(FolderFilter), SelectCategoryCommand) };
        chips.AddRange(distinctFolders.Select(f => new LibraryCategoryChip(f, string.Equals(f, FolderFilter, StringComparison.OrdinalIgnoreCase), SelectCategoryCommand)));
        Categories = new ObservableCollection<LibraryCategoryChip>(chips);
    }

    [RelayCommand]
    private async Task SelectCategoryAsync(string category)
    {
        FolderFilter = category == "All" ? string.Empty : category;
        await SearchAsync();
    }

    private static LibraryFileItem ToItem(SharedLibraryFileSummary summary)
    {
        var hasFolder = !string.IsNullOrWhiteSpace(summary.FolderPath);
        var sizeAndDate = $"{FormatSize(summary.SizeBytes)} · Updated {summary.UploadedAtUtc.LocalDateTime:g}";

        return new LibraryFileItem(
            summary.Id,
            summary.FileName,
            hasFolder ? summary.FolderPath : null,
            hasFolder,
            summary.Tags.Select(t => new LibraryTagItem(t)).ToList(),
            summary.Tags.Count > 0,
            sizeAndDate);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}

/// <summary>
/// One card in the library list — split into separate fields (rather than one pre-joined
/// SubtitleText, the pre-2026-09-06 shape) so the card template can render the folder path as an
/// accent-colored category line and the tags as individual chips, matching the redesign mockup
/// (https://claude.ai/code/artifact/7a7bebe1-6ea6-4df1-9908-b9bbdc900ccf) instead of one flat gray line.
/// </summary>
public sealed record LibraryFileItem(Guid Id, string FileName, string? FolderPath, bool HasFolder, IReadOnlyList<LibraryTagItem> Tags, bool HasTags, string SizeAndDateText);

/// <summary>Wraps a plain tag string only so it has a stable reference type for BindableLayout's ItemsSource — a bare List&lt;string&gt; binds fine too, but this keeps the DataTemplate's x:DataType explicit rather than implicitly "x:String".</summary>
public sealed record LibraryTagItem(string Label);

/// <summary>One category filter chip — carries the same shared SelectCategoryCommand instance (bound per-item as CommandParameter="{Binding Name}" in the DataTemplate) rather than requiring an x:Reference back to the page, since this item type doesn't live nested inside another item's own DataTemplate the way ChatThreadView's attachment chip does.</summary>
public sealed record LibraryCategoryChip(string Name, bool IsSelected, ICommand Command);
