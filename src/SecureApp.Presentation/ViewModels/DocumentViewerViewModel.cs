using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.ViewModels;

/// <summary>Displays one document, one rendered page at a time, via <see cref="IDocumentRenderingService"/>.</summary>
public sealed partial class DocumentViewerViewModel : ObservableObject, IQueryAttributable
{
    // Upper bound handed to IDocumentRenderingService.RenderPageAsync — a resolution cap, not
    // a fixed output size (see DocumentRenderingService's own remarks on that distinction).
    // 2026-09-30: raised from 1200x1600 for pinch-zoom (DocumentViewerPage.xaml.cs) — reading small
    // text while zoomed in means stretching this SAME bitmap via a Scale transform, not re-rendering
    // at higher resolution; a higher source resolution means less extreme Scale is needed for the
    // same readable result, which also means less strain on whatever's causing the glitching the
    // user saw ("pri urcitem... zvetseni se zacne obrazek glicovat") at the old cap's higher Scale values.
    private const int MaxRenderWidth = 1800;
    private const int MaxRenderHeight = 2400;

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
    /// 2026-10-03 — video/Office documents don't fit the paginated-raster model the rest of this
    /// viewer is built around. <see cref="IsVideo"/> swaps the pager/image for a MediaElement bound
    /// to <see cref="VideoSource"/> (a decrypted temp file, same CacheDirectory pattern
    /// <see cref="DownloadAsync"/> already uses). <see cref="IsExternalOnly"/> covers everything
    /// else unrenderable (DOCX/PPTX and any other unknown type, <see cref="DocumentType.Other"/>) —
    /// real in-app Office preview needs a commercial rendering library (Syncfusion or similar),
    /// deferred; "open in whatever app the user already has" is the pragmatic alternative instead.
    /// </summary>
    [ObservableProperty]
    public partial bool IsVideo { get; set; }

    [ObservableProperty]
    public partial MediaSource? VideoSource { get; set; }

    [ObservableProperty]
    public partial bool IsExternalOnly { get; set; }

    [ObservableProperty]
    public partial bool IsOpeningExternally { get; set; }

    public bool CanOpenExternally => !IsOpeningExternally;

    partial void OnIsOpeningExternallyChanged(bool value) => OnPropertyChanged(nameof(CanOpenExternally));

    /// <summary>True for the ordinary paginated Pdf/Image/PlainText/Spreadsheet path — gates the pager row (Předchozí/indikátor/Další), which has nothing to page through for <see cref="IsVideo"/>/<see cref="IsExternalOnly"/>. Computed, not a converter — same established pattern as the rest of this codebase.</summary>
    [ObservableProperty]
    public partial bool IsPagedDocument { get; set; } = true;

    partial void OnIsVideoChanged(bool value) => IsPagedDocument = !value && !IsExternalOnly;

    partial void OnIsExternalOnlyChanged(bool value) => IsPagedDocument = !value && !IsVideo;

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
        IsVideo = false;
        IsExternalOnly = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var document = await _documentRepository.GetByIdAsync(_documentId) ?? throw new DocumentNotFoundException(_documentId);
            Title = document.Title;

            if (document.DocumentType == DocumentType.Video)
            {
                await LoadVideoAsync(document);
                return;
            }
            if (document.DocumentType == DocumentType.Other)
            {
                IsExternalOnly = true;
                return;
            }

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

    private async Task LoadVideoAsync(Document document)
    {
        var plaintext = await _crypto.DecryptAsync(document.EncryptedContent);
        var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, document.FileName);
        await File.WriteAllBytesAsync(path, plaintext);
        VideoSource = MediaSource.FromFile(path);
        IsVideo = true;
    }

    /// <summary>DOCX/PPTX and anything else <see cref="DocumentType.Other"/> — decrypt to a temp file (same CacheDirectory pattern as <see cref="DownloadAsync"/>/<see cref="LoadVideoAsync"/>) and hand it to the OS to open in whatever app the device already has, rather than attempting an in-app preview.</summary>
    [RelayCommand]
    private async Task OpenExternallyAsync()
    {
        ErrorMessage = null;
        IsOpeningExternally = true;
        try
        {
            var document = await _documentRepository.GetByIdAsync(_documentId) ?? throw new DocumentNotFoundException(_documentId);
            var plaintext = await _crypto.DecryptAsync(document.EncryptedContent);

            var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, document.FileName);
            await File.WriteAllBytesAsync(path, plaintext);

            await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(
                new Microsoft.Maui.ApplicationModel.OpenFileRequest("Otevřít dokument", new Microsoft.Maui.Storage.ReadOnlyFile(path)));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se otevřít dokument: {ex.Message}";
        }
        finally
        {
            IsOpeningExternally = false;
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

    /// <summary>
    /// 2026-09-30, user's own ask: rename this document from the viewer itself (not just from the
    /// browser). Re-fetches rather than caching the document loaded by <see cref="LoadDocumentAsync"/>
    /// — same "always load fresh before mutating" caution <see cref="DownloadAsync"/> already takes,
    /// since nothing here guarantees the in-memory snapshot from the initial load is still current.
    /// </summary>
    [RelayCommand]
    private async Task RenameDocumentAsync(string? newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        try
        {
            var document = await _documentRepository.GetByIdAsync(_documentId) ?? throw new DocumentNotFoundException(_documentId);
            document.Rename(newTitle.Trim());
            await _documentRepository.UpdateAsync(document);
            Title = document.Title;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Přejmenování se nezdařilo: {ex.Message}";
        }
    }
}
