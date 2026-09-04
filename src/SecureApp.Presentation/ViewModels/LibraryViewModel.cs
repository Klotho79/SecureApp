using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;
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

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsUploading { get; set; }

    [ObservableProperty]
    public partial bool CanUpload { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    public LibraryViewModel(ISharedLibraryService libraryService)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));

        SearchQuery = string.Empty;
        FolderFilter = string.Empty;
        TagFilter = string.Empty;
        UploadFolderPath = string.Empty;
        UploadTags = string.Empty;
        Results = [];
        CanUpload = true;
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    partial void OnIsUploadingChanged(bool value) => CanUpload = !value;

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            var results = await _libraryService.SearchAsync(
                string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
                string.IsNullOrWhiteSpace(FolderFilter) ? null : FolderFilter,
                string.IsNullOrWhiteSpace(TagFilter) ? null : TagFilter);

            Results = new ObservableCollection<LibraryFileItem>(results.Select(ToItem));
            IsEmpty = Results.Count == 0;
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

    private static LibraryFileItem ToItem(SharedLibraryFileSummary summary)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(summary.FolderPath))
            parts.Add(summary.FolderPath);
        parts.Add(FormatSize(summary.SizeBytes));
        if (summary.Tags.Count > 0)
            parts.Add(string.Join(", ", summary.Tags));
        parts.Add(summary.UploadedAtUtc.LocalDateTime.ToString("g"));

        return new LibraryFileItem(summary.Id, summary.FileName, string.Join(" · ", parts));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}

public sealed record LibraryFileItem(Guid Id, string FileName, string SubtitleText);
