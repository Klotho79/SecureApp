using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// "Rozpracované dokumenty" (2026-10-26, user: "pro admina a modera seznam a náhled tvořených dokumentů" — only the
/// unfinished and the waiting ones): everything still being written (Koncept), waiting for review (Čeká na schválení)
/// or sent back (Vráceno k přepracování), from every author. Opening one is exactly what the review queue does — the
/// latest version's file is downloaded and shown in the ordinary viewer, read-only.
/// </summary>
public sealed partial class InProgressDocumentsViewModel : ObservableObject
{
    private readonly ILibraryReviewService _libraryReviewService;
    private readonly ISharedLibraryService _sharedLibraryService;

    public InProgressDocumentsViewModel(ILibraryReviewService libraryReviewService, ISharedLibraryService sharedLibraryService)
    {
        _libraryReviewService = libraryReviewService ?? throw new ArgumentNullException(nameof(libraryReviewService));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
    }

    [ObservableProperty]
    public partial ObservableCollection<InProgressItem> Items { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorText = null;
        try
        {
            var documents = await _libraryReviewService.GetInProgressAsync();
            Items = new ObservableCollection<InProgressItem>(documents.Select(ToItem));
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorText = $"Nepodařilo se načíst rozpracované dokumenty: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync(InProgressItem? item)
    {
        if (item is null) return;

        ErrorText = null;
        try
        {
            var detail = await _libraryReviewService.GetDetailAsync(item.Id);
            if (detail.Versions.Count == 0)
            {
                ErrorText = $"'{item.Title}' zatím nemá uloženou žádnou verzi, není co zobrazit.";
                return;
            }
            var latest = detail.Versions.OrderByDescending(v => v.VersionNumber).First();
            var document = await _sharedLibraryService.DownloadAndImportAsync(latest.LibraryFileId);
            await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}", animate: false);
        }
        catch (Exception ex)
        {
            ErrorText = $"Nepodařilo se otevřít '{item.Title}': {ex.Message}";
        }
    }

    private static InProgressItem ToItem(InProgressDocument document)
    {
        var summary = document.Summary;
        var (status, isWaiting) = summary.Status switch
        {
            LibraryDocumentStatus.PendingReview => ("Čeká na schválení", true),
            LibraryDocumentStatus.Rejected => ("Vráceno k přepracování", false),
            _ => ("Koncept", false),
        };
        return new InProgressItem(
            summary.Id,
            summary.Title,
            summary.FolderPath,
            document.AuthorName,
            status,
            isWaiting,
            $"Naposledy změněno {summary.UpdatedAtUtc.LocalDateTime:d. M. yyyy H:mm}");
    }
}

/// <summary>One row of the in-progress overview.</summary>
public sealed record InProgressItem(Guid Id, string Title, string FolderPath, string AuthorName, string StatusText, bool IsWaiting, string UpdatedText);
