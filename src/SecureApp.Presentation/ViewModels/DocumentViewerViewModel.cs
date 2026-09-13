using System.Net.NetworkInformation;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
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

    // Milestone 4, Task 4.4: cycles through a fixed set of on-canvas offsets rather than
    // computing against the page's actual pixel size (unknown from the ViewModel) — modest
    // enough to stay roughly on-screen on both phone and desktop, moving is what defeats a
    // static crop/inpaint of the watermark, exact placement doesn't need to be precise.
    private static readonly (double Dx, double Dy)[] WatermarkOffsets =
    [
        (-100, -200), (100, -200), (0, 0), (-100, 200), (100, 200)
    ];

    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentRenderingService _renderingService;
    private readonly ICurrentUserService _currentUserService;

    private Guid _documentId;
    private int _watermarkOffsetIndex;
    private string? _cachedLocalIp;
    private IDispatcherTimer? _watermarkTimer;

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
    public partial string WatermarkText { get; set; }

    [ObservableProperty]
    public partial double WatermarkTranslationX { get; set; }

    [ObservableProperty]
    public partial double WatermarkTranslationY { get; set; }

    public DocumentViewerViewModel(
        IDocumentRepository documentRepository,
        IDocumentRenderingService renderingService,
        ICurrentUserService currentUserService)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _renderingService = renderingService ?? throw new ArgumentNullException(nameof(renderingService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

        Title = "Dokument";
        PageIndicatorText = string.Empty;
        WatermarkText = string.Empty;
    }

    partial void OnErrorMessageChanged(string? value) => HasError = !string.IsNullOrEmpty(value);

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
    /// Milestone 4, Task 4.4: starts the moving username+timestamp+IP watermark overlay.
    /// Call from the page's OnAppearing (paired with <see cref="StopWatermark"/> in
    /// OnDisappearing) rather than the constructor — this touches <see cref="IDispatcherTimer"/>,
    /// which needs a live app/dispatcher.
    /// </summary>
    public void StartWatermark()
    {
        UpdateWatermark();

        if (_watermarkTimer is not null) return; // already running — OnAppearing can re-fire on a page popped back to, not just on first navigation

        _watermarkTimer = Microsoft.Maui.Controls.Application.Current?.Dispatcher.CreateTimer();
        if (_watermarkTimer is null) return;

        _watermarkTimer.Interval = TimeSpan.FromSeconds(3);
        _watermarkTimer.Tick += (_, _) => UpdateWatermark();
        _watermarkTimer.Start();
    }

    public void StopWatermark() => _watermarkTimer?.Stop();

    private void UpdateWatermark()
    {
        var ip = _cachedLocalIp ??= GetLocalIpAddress(); // doesn't change mid-session; avoid re-enumerating NICs every tick
        WatermarkText = $"{_currentUserService.Current.DisplayName} • {DateTime.Now:yyyy-MM-dd HH:mm:ss} • {ip}";

        _watermarkOffsetIndex = (_watermarkOffsetIndex + 1) % WatermarkOffsets.Length;
        (WatermarkTranslationX, WatermarkTranslationY) = WatermarkOffsets[_watermarkOffsetIndex];
    }

    /// <summary>
    /// Local network IP only — deliberately not the public/external IP, which would need an
    /// outbound call to a third-party lookup service. Every other network access in this app
    /// is local-only (SQLCipher DB, MAUI SecureStorage); adding the app's first-ever external
    /// HTTP dependency just for a watermark string didn't seem worth it, especially for a
    /// DLP-focused app where an unexpected outbound call is itself a minor red flag.
    /// </summary>
    private static string GetLocalIpAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        return address.Address.ToString();
                }
            }
        }
        catch
        {
            // Best-effort only — the watermark still shows user + timestamp without an IP.
        }

        return "neznámá";
    }
}
