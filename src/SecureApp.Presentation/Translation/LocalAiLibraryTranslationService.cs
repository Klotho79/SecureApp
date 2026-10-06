// Windows-only (2026-10-06, real crash fix): this type references QuestPDF/PdfPig directly, and
// those packages must never be referenced on Android/iOS at all — their native libraries crashed
// the whole app at startup on Android before this guard (see MauiProgram.cs's and the .csproj's own
// remarks). The whole file compiles out on every other platform; UnsupportedLibraryTranslationService
// is what's registered there instead.
#if WINDOWS
using System.Net.Http.Json;
using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Translation;

/// <inheritdoc cref="ILibraryTranslationService"/>
/// <remarks>
/// Lives in Presentation alongside <see cref="Library.HttpSharedLibraryService"/>/<see cref="Rendering.DocumentRenderingService"/>
/// for the same reason — needs <c>HttpClient</c> (the local Ollama/LM Studio endpoint) + PdfPig +
/// QuestPDF, none of which Domain may reference.
///
/// Pipeline per translation run: download+decrypt the original PDF → read its real embedded text
/// layer page-by-page via PdfPig (no OCR — these are electronic/born-digital documents, per the
/// user's own correction mid-planning) → translate each page's text via the admin's local model →
/// build a brand-new PDF via QuestPDF, interleaving each original page's rendered image with its
/// own translated-text page → upload + wrap in a new Draft <see cref="LibraryDocumentSummary"/> →
/// submit for review. Reuses <see cref="IDocumentRenderingService"/>/<see cref="ISharedLibraryService"/>/
/// <see cref="ILibraryReviewService"/> entirely as-is — no relay changes, no new review UI.
///
/// All-or-nothing failure policy (flagged to the user in the plan, not silently decided): a
/// sustained AI-call failure mid-document aborts the WHOLE translation — nothing partial gets
/// uploaded, no Draft created. Transient failures get <see cref="MaxRetries"/> retries first.
/// </remarks>
public sealed class LocalAiLibraryTranslationService : ILibraryTranslationService
{
    private const int MaxRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDocumentRenderingService _renderingService;
    private readonly ISharedLibraryService _sharedLibraryService;
    private readonly ILibraryReviewService _libraryReviewService;
    private readonly ICryptoService _crypto;

    // One vision-free text call per page on a local GPU; generous per-call budget since the user's
    // own model/hardware choice (not this app's) determines actual latency.
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(3) };

    public LocalAiLibraryTranslationService(
        IDocumentRenderingService renderingService,
        ISharedLibraryService sharedLibraryService,
        ILibraryReviewService libraryReviewService,
        ICryptoService crypto)
    {
        _renderingService = renderingService ?? throw new ArgumentNullException(nameof(renderingService));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
        _libraryReviewService = libraryReviewService ?? throw new ArgumentNullException(nameof(libraryReviewService));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        var baseUrl = GetConfiguredBaseUrl();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PingTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "v1/models"));
            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Místní AI server na {baseUrl} odpověděl chybou {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Místní AI server na {baseUrl} neodpovídá (vypršel časový limit). Zkontrolujte, že Ollama/LM Studio běží a je dostupné na této adrese.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Místní AI server na {baseUrl} není dostupný: {ex.Message}");
        }
    }

    public async Task<LibraryDocumentSummary> TranslateAndSubmitAsync(
        Guid sourceLibraryFileId,
        string sourceTitle,
        string sourceFolderPath,
        string targetLanguage,
        IProgress<double>? onProgress = null,
        IProgress<string>? onStatusText = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        onStatusText?.Report("Stahuji originální dokument…");
        var sourceDocument = await _sharedLibraryService.DownloadAndImportAsync(sourceLibraryFileId, ct: ct);
        if (sourceDocument.DocumentType != DocumentType.Pdf)
            throw new InvalidOperationException("Automatický překlad podporuje jen PDF dokumenty.");

        var modelName = LocalAiTranslationSettings.GetModelName();
        if (string.IsNullOrWhiteSpace(modelName))
            throw new InvalidOperationException("Nejprve v Nastavení (Admin: Místní AI překlad) vyplňte název modelu.");
        var baseUrl = GetConfiguredBaseUrl();

        onStatusText?.Report("Kontroluji dostupnost místního AI modelu…");
        await PingAsync(ct);

        onStatusText?.Report("Dešifruji originální dokument…");
        var rawPdfBytes = await _crypto.DecryptAsync(sourceDocument.EncryptedContent, ct);

        var pageCount = await _renderingService.GetPageCountAsync(sourceDocument.Id, ct);
        var originalPagePngs = new List<byte[]>(pageCount);
        var translatedPages = new List<string>(pageCount);

        using var pdfPig = UglyToad.PdfPig.PdfDocument.Open(rawPdfBytes);
        for (var i = 0; i < pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            onStatusText?.Report($"Stránka {i + 1}/{pageCount} — zpracovávám…");

            var rendered = await _renderingService.RenderPageAsync(sourceDocument.Id, i, maxWidth: 1800, maxHeight: 2400, ct);
            originalPagePngs.Add(rendered.PixelData);

            // Real embedded text layer, not OCR — these are electronic/born-digital PDFs (user's own
            // correction mid-planning). A page with no extractable text (an actual scanned image
            // hiding inside an otherwise-electronic PDF — not expected to be common) is handled
            // gracefully, not as an error: no AI call for it, just a placeholder note, since the
            // original page's image is still included either way. OCR/vision fallback for this case
            // is explicitly deferred to a future pass, not built now.
            var pageText = i < pdfPig.NumberOfPages ? pdfPig.GetPage(i + 1).Text : string.Empty;
            string translatedText;
            if (string.IsNullOrWhiteSpace(pageText))
            {
                translatedText = "[Tato strana nemá rozpoznatelný text — viz obrázek originální strany.]";
            }
            else
            {
                onStatusText?.Report($"Stránka {i + 1}/{pageCount} — překládám…");
                translatedText = await TranslatePageWithRetryAsync(baseUrl, modelName, pageText, i, targetLanguage, ct);
            }
            translatedPages.Add(translatedText);
            onProgress?.Report((i + 1) / (double)pageCount * 0.8);
        }

        onStatusText?.Report("Sestavuji výsledné PDF…");
        var outputPdfBytes = BuildInterleavedPdf(originalPagePngs, translatedPages, targetLanguage);
        onProgress?.Report(0.85);

        onStatusText?.Report("Nahrávám do knihovny…");
        var baseName = string.IsNullOrWhiteSpace(sourceTitle) ? sourceDocument.Title : sourceTitle;
        var fileName = $"{baseName} ({targetLanguage}).pdf";
        using var uploadStream = new MemoryStream(outputPdfBytes);
        var uploaded = await _sharedLibraryService.UploadAsync(sourceFolderPath, fileName, tags: [], uploadStream, listed: false, ct: ct);
        onProgress?.Report(0.9);

        onStatusText?.Report("Vytvářím koncept dokumentu…");
        // A brand-new LibraryDocument, deliberately NOT a new version of the original
        // (AddVersionAsync) — approving a new version unlists/replaces the previous one, which would
        // make the original source document disappear the moment the translation gets approved. The
        // two need to coexist.
        var title = $"{baseName} ({targetLanguage} překlad)";
        var created = await _libraryReviewService.CreateDraftAsync(
            title, sourceFolderPath, uploaded.Id,
            changeNote: $"Automatický AI překlad do jazyka: {targetLanguage} (model: {modelName}).", ct);
        onProgress?.Report(0.95);

        onStatusText?.Report("Odesílám ke schválení…");
        var submitted = await _libraryReviewService.SubmitForReviewAsync(created.Id, ct);
        onProgress?.Report(1.0);
        onStatusText?.Report("Hotovo.");
        return submitted;
    }

    private async Task<string> TranslatePageWithRetryAsync(Uri baseUrl, string model, string pageText, int pageIndex, string targetLanguage, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await TranslatePageTextAsync(baseUrl, model, pageText, targetLanguage, ct);
            }
            catch (Exception ex) when (attempt < MaxRetries && ex is not OperationCanceledException)
            {
                await Task.Delay(RetryDelay, ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Překlad strany {pageIndex + 1} se nezdařil: {ex.Message}", ex);
            }
        }
    }

    private async Task<string> TranslatePageTextAsync(Uri baseUrl, string model, string pageText, string targetLanguage, CancellationToken ct)
    {
        var systemPrompt =
            $"You are a precise medical-document translator. Translate the given page text into {targetLanguage}. " +
            "Preserve exact medical terminology, drug names, dosages, units, and numeric values exactly as " +
            "written — never round or convert them. If the text includes a table rendered as plain text, keep " +
            "it as readable running text (the original page image is shown separately for exact layout). " +
            "Output ONLY the translated text, no commentary.";

        var requestBody = new ChatCompletionRequest(
            model,
            0.2,
            [
                new ChatMessageRequest("system", systemPrompt),
                new ChatMessageRequest("user", pageText),
            ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "v1/chat/completions"))
        {
            Content = JsonContent.Create(requestBody, options: HttpJsonOptions),
        };
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(HttpJsonOptions, ct)
            ?? throw new InvalidOperationException("Místní AI model vrátil prázdnou odpověď.");
        if (dto.Choices.Count == 0)
            throw new InvalidOperationException("Místní AI model vrátil odpověď bez žádného výsledku.");
        return dto.Choices[0].Message.Content;
    }

    private static Uri GetConfiguredBaseUrl()
    {
        var text = LocalAiTranslationSettings.GetBaseUrl();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var baseUrl))
            throw new InvalidOperationException("Adresa místního AI serveru v Nastavení není platná.");
        return baseUrl;
    }

    /// <summary>
    /// For each original page: the original page's rendered image (pixel-identical — trivially
    /// satisfies "visually close to original" for every graph/diagram/table), immediately followed
    /// by a page of the translated text. Stamps every translated page with its original page number
    /// so the correspondence survives even if QuestPDF's own word-wrap/overflow pushes a translated
    /// page's text onto extra physical pages (deliberately using QuestPDF's own text layout here, not
    /// porting DocumentRenderingService's WrapText helper).
    /// </summary>
    private static byte[] BuildInterleavedPdf(List<byte[]> originalPagePngs, List<string> translatedPages, string targetLanguage)
    {
        return Document.Create(container =>
        {
            for (var i = 0; i < originalPagePngs.Count; i++)
            {
                var pageNumber = i + 1;
                var pageCount = originalPagePngs.Count;
                var originalImage = originalPagePngs[i];
                var translatedText = translatedPages[i];

                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.Content().Image(originalImage).FitArea();
                });
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.Header().Text($"Překlad — strana {pageNumber}/{pageCount} originálu ({targetLanguage})").FontSize(9).Italic();
                    page.Content().PaddingTop(10).Text(translatedText).FontSize(11);
                });
            }
        }).GeneratePdf();
    }

    private sealed record ChatCompletionRequest(string Model, double Temperature, List<ChatMessageRequest> Messages);
    private sealed record ChatMessageRequest(string Role, string Content);
    private sealed record ChatCompletionResponse(List<ChatChoice> Choices);
    private sealed record ChatChoice(ChatResponseMessage Message);
    private sealed record ChatResponseMessage(string Content);
}
#endif
