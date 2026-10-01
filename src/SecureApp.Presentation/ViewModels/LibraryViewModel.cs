using System.Collections.ObjectModel;
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
    private readonly IDiagnosticsReporter _diagnosticsReporter;
    private readonly ILibraryReviewService _libraryReviewService;
    private readonly IDevicePolicyService _devicePolicyService;

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
    /// Category names (2026-09-06, reworked same day from a chip/pill row to a plain Picker —
    /// the user's own call: this is a professional reference tool, not a consumer app, and the
    /// pill row both looked out of place ("nelibi se mi ty oblacky") and had a real layout bug on
    /// narrow screens where the horizontal ScrollView could visually overlap the sibling field
    /// below it). Deliberately NOT a fixed/hardcoded list like the redesign mockup's static
    /// "Emergency/Surgery/Pediatrics/…" row: every real community using this app picks its own
    /// categories by typing a folder name on upload (already-existing, unchanged behavior), so
    /// this list is derived from whatever folder names actually exist in the library right now,
    /// refreshed alongside every search. "All" (always first) clears the filter.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<string> Categories { get; set; }

    /// <summary>Bound to the Picker's SelectedItem; see <see cref="OnSelectedCategoryChanged"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedCategory { get; set; }

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

    /// <summary>
    /// Document Library content-approval workflow (2026-10-01). "Moje koncepty" — documents this
    /// device created or submitted, across any status (Draft/PendingReview/Published/Rejected).
    /// Separate from <see cref="Results"/> (the ordinary published-only browse list), since a Draft or
    /// PendingReview document is deliberately invisible there (its library_files row is unlisted).
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<LibraryDocumentDraftItem> MyDocuments { get; set; }

    [ObservableProperty]
    public partial bool HasMyDocuments { get; set; }

    /// <summary>Whether THIS device may review other members' submissions — gates the "📋 Ke schválení" entry point. Read fresh on every page appearance, not cached, since an admin can grant/revoke it at any time.</summary>
    [ObservableProperty]
    public partial bool IsDocumentReviewer { get; set; }

    public LibraryViewModel(
        ISharedLibraryService libraryService,
        ICurrentUserService currentUserService,
        IDiagnosticsReporter diagnosticsReporter,
        ILibraryReviewService libraryReviewService,
        IDevicePolicyService devicePolicyService)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _libraryReviewService = libraryReviewService ?? throw new ArgumentNullException(nameof(libraryReviewService));
        _devicePolicyService = devicePolicyService ?? throw new ArgumentNullException(nameof(devicePolicyService));

        SearchQuery = string.Empty;
        FolderFilter = string.Empty;
        TagFilter = string.Empty;
        UploadFolderPath = string.Empty;
        UploadTags = string.Empty;
        Results = [];
        Categories = [];
        SelectedCategory = "Vše";
        CanModifyContent = true;
        MyDocuments = [];
        RecomputeCanUpload();
    }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Error, value!, nameof(LibraryViewModel));
    }

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
            // Search-latency telemetry (2026-10-01, the AIM-spec-derived requirement adapted to this
            // app's existing AppLog metrics pipeline rather than a new logging system — see AppLog's
            // own remarks). Target per the spec was <30ms for a LOCAL indexed query; this one is a
            // network round trip to the relay, so the number is expected to run much higher — logged
            // for visibility/trending, not held to that local-query target.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await _libraryService.SearchAsync(
                string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
                string.IsNullOrWhiteSpace(FolderFilter) ? null : FolderFilter,
                string.IsNullOrWhiteSpace(TagFilter) ? null : TagFilter);
            Infrastructure.AppLog.Metric("search_latency.library", sw.Elapsed.TotalMilliseconds, "ms", ("resultCount", results.Count));

            Results = new ObservableCollection<LibraryFileItem>(results.Select(ToItem));
            IsEmpty = Results.Count == 0;

            await RefreshCategoriesAsync();
            await RefreshReviewWorkflowStateAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se prohledat knihovnu: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Best-effort by design (2026-10-01) — a failure here must never block the ordinary library
    /// browse/search above from working; "Moje koncepty" and the reviewer entry point simply stay
    /// empty/hidden until the next successful refresh, same posture as the rest of this screen's
    /// network-dependent extras.
    /// </summary>
    private async Task RefreshReviewWorkflowStateAsync()
    {
        try
        {
            var policy = await _devicePolicyService.GetMyPolicyAsync();
            IsDocumentReviewer = policy.IsDocumentReviewer;

            var mine = await _libraryReviewService.GetMyDocumentsAsync();
            MyDocuments = new ObservableCollection<LibraryDocumentDraftItem>(mine.Select(ToDraftItem));
            HasMyDocuments = MyDocuments.Count > 0;
        }
        catch
        {
            // Best-effort — see this method's own remarks.
        }
    }

    private static LibraryDocumentDraftItem ToDraftItem(LibraryDocumentSummary summary) => new(
        summary.Id,
        summary.Title,
        StatusLabel(summary.Status),
        summary.Status is LibraryDocumentStatus.Draft or LibraryDocumentStatus.Rejected);

    private static string StatusLabel(LibraryDocumentStatus status) => status switch
    {
        LibraryDocumentStatus.Draft => "Koncept",
        LibraryDocumentStatus.PendingReview => "Čeká na schválení",
        LibraryDocumentStatus.Published => "Publikováno",
        LibraryDocumentStatus.Rejected => "Zamítnuto",
        _ => status.ToString()
    };

    /// <summary>
    /// A starting structure so the chip row isn't empty before anyone has uploaded anything — the
    /// user's own field (anesthesiology/intensive care) plus a general announcements bucket. Not
    /// exclusive: any other folder name typed on upload shows up as its own chip too (see
    /// RefreshCategoriesAsync below), this is just a floor, not a ceiling. Worth making
    /// admin-editable later rather than a hardcoded list, if the community's categories evolve.
    /// </summary>
    private static readonly string[] SeedCategories = ["Anesteziologie", "Intenzivní medicína", "Oznámení"];

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
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var names = new List<string> { "Vše" };
        names.AddRange(distinctFolders);
        Categories = new ObservableCollection<string>(names);

        // Keep the Picker's selection in sync with whatever FolderFilter already is (e.g. after a
        // plain text search) without re-triggering OnSelectedCategoryChanged below — assigning the
        // same string value again is a no-op per CommunityToolkit.Mvvm's generated setter.
        SelectedCategory = string.IsNullOrEmpty(FolderFilter)
            ? "Vše"
            : names.FirstOrDefault(n => string.Equals(n, FolderFilter, StringComparison.OrdinalIgnoreCase)) ?? FolderFilter;
    }

    /// <summary>
    /// Fires only on an actual user pick in the Picker (see the RefreshCategoriesAsync remark
    /// above for why re-assigning the same value here is silent, avoiding a refresh↔selection
    /// feedback loop).
    /// </summary>
    partial void OnSelectedCategoryChanged(string? value)
    {
        if (value is null)
        {
            return;
        }

        var target = value == "Vše" ? string.Empty : value;
        if (string.Equals(target, FolderFilter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        FolderFilter = target;
        _ = SearchAsync();
    }

    private static LibraryFileItem ToItem(SharedLibraryFileSummary summary)
    {
        var hasFolder = !string.IsNullOrWhiteSpace(summary.FolderPath);
        var sizeAndDate = $"{FormatSize(summary.SizeBytes)} · Aktualizováno {summary.UploadedAtUtc.LocalDateTime:g}";

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

/// <summary>One row in "Moje koncepty" (2026-10-01) — <see cref="CanSubmit"/> gates the "Odeslat ke schválení" button, true only for Draft/Rejected (a PendingReview or already-Published document has nothing to (re)submit).</summary>
public sealed record LibraryDocumentDraftItem(Guid Id, string Title, string StatusText, bool CanSubmit);
