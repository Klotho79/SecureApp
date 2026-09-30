using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.ViewModels;

/// <summary>Displays one document, one rendered page at a time, via <see cref="IDocumentRenderingService"/>.</summary>
public sealed partial class DocumentViewerViewModel : ObservableObject, IQueryAttributable
{
    // Upper bound handed to IDocumentRenderingService.RenderPageAsync — a resolution cap, not
    // a fixed output size (see DocumentRenderingService's own remarks on that distinction).
    private const int MaxRenderWidth = 1200;
    private const int MaxRenderHeight = 1600;

    private const double MinZoom = 1.0;
    private const double MaxZoom = 4.0;
    private const double ZoomStep = 0.5;

    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentRenderingService _renderingService;
    private readonly ICryptoService _crypto;
    private readonly IDocumentDownloadLogService _downloadLog;

    private Guid _documentId;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial int PageCount { get; set; }

    [ObservableProperty]
    public partial int CurrentPageNumber { get; set; } // 1-based, for display

    [ObservableProperty]
    public partial string PageIndicatorText { get; set; }

    [ObservableProperty]
    public partial ImageSource? CurrentPageImage { get; set; }

    [ObservableProperty]
    public partial bool HasImage { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool CanGoToPreviousPage { get; set; }

    [ObservableProperty]
    public partial bool CanGoToNextPage { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial bool CanDownload { get; set; } = true;

    [ObservableProperty]
    public partial string? DownloadErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasDownloadError { get; set; }

    /// <summary>
    /// 2026-09-30, user's own ask, replacing the removed visible watermark overlay (which made
    /// documents unreadable with no way to zoom past it — "nejde ani číst ani zvětšit"): simple
    /// +/- buttons scale the rendered page image; bound to <c>Image.Scale</c> in the page's own
    /// XAML, inside a ScrollView so a zoomed-in page can be panned. No pinch gesture yet — this is
    /// the reliable cross-platform (touch AND mouse-click) baseline; pinch can be layered on later
    /// if actually asked for.
    /// </summary>
    [ObservableProperty]
    public partial double ImageScale { get; set; } = MinZoom;

    [ObservableProperty]
    public partial bool CanZoomIn { get; set; } = true;

    [ObservableProperty]
    public partial bool CanZoomOut { get; set; }

    public DocumentViewerViewModel(
        IDocumentRepository documentRepository,
        IDocumentRenderingService renderingService,
        ICryptoService crypto,
        IDocumentDownloadLogService downloadLog)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _renderingService = renderingService ?? throw new ArgumentNullException(nameof(renderingService));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _downloadLog = downloadLog ?? throw new ArgumentNullException(nameof(downloadLog));

        Title = "Dokument";
        PageIndicatorText = string.Empty;
    }

    partial void OnErrorMessageChanged(string? value) => HasError = !string.IsNullOrEmpty(value);

    partial void OnDownloadErrorMessageChanged(string? value) => HasDownloadError = !string.IsNullOrEmpty(value);

    partial void OnIsDownloadingChanged(bool value) => CanDownload = !value;

    partial void OnCurrentPageImageChanged(ImageSource? value) => HasImage = value is not null;

    partial void OnCurrentPageNumberChanged(int value) => UpdatePageIndicator();

    partial void OnPageCountChanged(int value) => UpdatePageIndicator();

    private void UpdatePageIndicator() => PageIndicatorText = PageCount == 0 ? string.Empty : $"Stránka {CurrentPageNumber} z {PageCount}";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("documentId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _documentId = id;
    }

    [RelayCommand]
    private async Task LoadDocumentAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var document = await _documentRepository.GetByIdAsync(_documentId) ?? throw new DocumentNotFoundException(_documentId);
            Title = document.Title;
            var tMeta = sw.Elapsed.TotalMilliseconds;
            PageCount = await _renderingService.GetPageCountAsync(_documentId);
            var tPageCount = sw.Elapsed.TotalMilliseconds;

            CurrentPageNumber = 0; // forces RenderCurrentPageAsync below to actually render page 1
            await GoToPageAsync(1);
            SecureApp.Presentation.Infrastructure.AppLog.Metric("doc.open", sw.Elapsed.TotalMilliseconds, "ms",
                ("meta", System.Math.Round(tMeta, 1)), ("pageCount", System.Math.Round(tPageCount - tMeta, 1)), ("pages", PageCount));
        }
        catch (NotSupportedException ex)
        {
            ErrorMessage = ex.Message;
            SecureApp.Presentation.Infrastructure.AppLog.Error("DocumentViewer.Load", "unsupported document", ex);
        }
        catch (DocumentNotFoundException)
        {
            ErrorMessage = "Tento dokument se nepodařilo najít.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 2026-09-30, user's own ask: decrypts the ORIGINAL file content (not a rendered page) and hands
    /// it to the OS share sheet so the user picks where it lands outside the app — then logs the
    /// download (who + when + which document) to the relay, admin-searchable (see
    /// IDocumentDownloadLogService's own remarks). The log call is best-effort by design (it swallows
    /// its own failures) — a relay hiccup must never block the user from actually getting their file.
    /// </summary>
    [RelayCommand]
    private async Task DownloadAsync()
    {
        DownloadErrorMessage = null;
        IsDownloading = true;
        try
        {
            var document = await _documentRepository.GetByIdAsync(_documentId) ?? throw new DocumentNotFoundException(_documentId);
            var plaintext = await _crypto.DecryptAsync(document.EncryptedContent);

            var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, document.FileName);
            await File.WriteAllBytesAsync(path, plaintext);

            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(
                new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFileRequest
                {
                    Title = "Uložit dokument mimo appku",
                    File = new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFile(path),
                });

            await _downloadLog.LogAsync(document.Title, document.SourceLibraryFileId);
        }
        catch (Exception ex)
        {
            DownloadErrorMessage = $"Stažení se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private Task NextPageAsync() => GoToPageAsync(CurrentPageNumber + 1);

    [RelayCommand]
    private Task PreviousPageAsync() => GoToPageAsync(CurrentPageNumber - 1);

    private async Task GoToPageAsync(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > PageCount || pageNumber == CurrentPageNumber)
            return;

        IsLoading = true;
        try
        {
            // Rasterize off the UI thread (2026-09-11) so decoding/resizing a large image never
            // stutters the animation or the UI — the continuation resumes on the UI thread for the
            // (cheap) ImageSource assignment.
            var rsw = System.Diagnostics.Stopwatch.StartNew();
            var rendered = await Task.Run(() => _renderingService.RenderPageAsync(_documentId, pageNumber - 1, MaxRenderWidth, MaxRenderHeight));
            SecureApp.Presentation.Infrastructure.AppLog.Metric("doc.render", rsw.Elapsed.TotalMilliseconds, "ms", ("page", pageNumber), ("bytes", rendered.PixelData.Length));
            CurrentPageImage = ImageSource.FromStream(() => new MemoryStream(rendered.PixelData));
            CurrentPageNumber = pageNumber;
            CanGoToPreviousPage = CurrentPageNumber > 1;
            CanGoToNextPage = CurrentPageNumber < PageCount;
            ImageScale = MinZoom; // a fresh page always starts unzoomed — a leftover zoom from the previous page would be confusing
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentOutOfRangeException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ZoomIn() => ImageScale = Math.Min(MaxZoom, ImageScale + ZoomStep);

    [RelayCommand]
    private void ZoomOut() => ImageScale = Math.Max(MinZoom, ImageScale - ZoomStep);

    partial void OnImageScaleChanged(double value)
    {
        CanZoomIn = value < MaxZoom;
        CanZoomOut = value > MinZoom;
    }
}
