using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Document Library review queue (2026-10-01, the content-approval workflow) — lists every document
/// currently awaiting review and lets a Reviewer/Admin device open, approve, or reject the submission.
/// Opening a pending item reuses <see cref="ISharedLibraryService.DownloadAndImportAsync"/> + the
/// existing <see cref="DocumentViewerPage"/> verbatim (zero new viewer code) — the pending version's
/// own (unlisted) library file is just an ordinary library file to that machinery.
/// </summary>
public sealed partial class LibraryReviewQueueViewModel : ObservableObject
{
    private readonly ILibraryReviewService _libraryReviewService;
    private readonly ISharedLibraryService _sharedLibraryService;

    [ObservableProperty]
    public partial ObservableCollection<ReviewQueueItem> PendingItems { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    public LibraryReviewQueueViewModel(ILibraryReviewService libraryReviewService, ISharedLibraryService sharedLibraryService)
    {
        _libraryReviewService = libraryReviewService ?? throw new ArgumentNullException(nameof(libraryReviewService));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            var pending = await _libraryReviewService.GetPendingReviewAsync();
            PendingItems = new ObservableCollection<ReviewQueueItem>(pending.Select(ToItem));
            IsEmpty = PendingItems.Count == 0;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se načíst frontu ke schválení: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenItemAsync(ReviewQueueItem? item)
    {
        if (item is null) return;

        StatusErrorMessage = null;
        try
        {
            var detail = await _libraryReviewService.GetDetailAsync(item.Id);
            var latestVersion = detail.Versions.OrderByDescending(v => v.VersionNumber).First();
            var document = await _sharedLibraryService.DownloadAndImportAsync(latestVersion.LibraryFileId);
            await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}", animate: false);
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít '{item.Title}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApproveAsync(ReviewQueueItem? item)
    {
        if (item is null) return;

        StatusErrorMessage = null;
        try
        {
            var comment = await (Shell.Current?.CurrentPage?.DisplayPromptAsync("Schválit a publikovat", "Volitelný komentář:") ?? Task.FromResult<string?>(null));
            await _libraryReviewService.ReviewAsync(item.Id, approve: true, comment);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Schválení '{item.Title}' se nezdařilo: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RejectAsync(ReviewQueueItem? item)
    {
        if (item is null) return;

        StatusErrorMessage = null;
        var comment = await (Shell.Current?.CurrentPage?.DisplayPromptAsync("Zamítnout", "Důvod zamítnutí (povinné):") ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(comment))
        {
            StatusErrorMessage = "Při zamítnutí je komentář povinný.";
            return;
        }

        try
        {
            await _libraryReviewService.ReviewAsync(item.Id, approve: false, comment);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Zamítnutí '{item.Title}' se nezdařilo: {ex.Message}";
        }
    }

    private static ReviewQueueItem ToItem(LibraryDocumentSummary summary) => new(
        summary.Id,
        summary.Title,
        summary.FolderPath,
        summary.SubmittedAtUtc is { } submitted ? $"Odesláno {submitted.LocalDateTime:d.M. HH:mm}" : string.Empty);
}

/// <summary>One row in the review queue.</summary>
public sealed record ReviewQueueItem(Guid Id, string Title, string FolderPath, string SubmittedAtText);
