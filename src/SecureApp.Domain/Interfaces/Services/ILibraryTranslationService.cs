using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Admin-only, Windows-only local-AI PDF translation (2026-10-05, user's own ask). Reads an
/// existing, already-published PDF library file's real embedded text layer page-by-page (no OCR —
/// these are electronic/born-digital documents), translates each page's text via a vision-free local
/// LLM (Ollama/LM Studio, both running on the admin's own PC, OpenAI-compatible
/// <c>/v1/chat/completions</c>), and produces a brand-new interleaved PDF (original page image, then
/// its translated-text page, repeated for every page) that is uploaded as a new private library file,
/// wrapped in a new Draft <see cref="LibraryDocumentSummary"/>, and immediately submitted for review —
/// landing in the existing <c>LibraryReviewQueuePage</c> with zero new viewer code.
///
/// Lives in Domain as a pure port (only BCL/Domain types in this interface); the only implementation
/// (<c>LocalAiLibraryTranslationService</c>) is in Presentation because it needs <c>HttpClient</c> +
/// PdfPig + QuestPDF — same Domain/Presentation split as <see cref="IDocumentRenderingService"/>/
/// <see cref="IUpdateService"/>.
/// </summary>
public interface ILibraryTranslationService
{
    /// <summary>
    /// <paramref name="sourceLibraryFileId"/> must be an already-listed (published) library file
    /// whose content is a real PDF. Returns the new Draft-then-PendingReview
    /// <see cref="LibraryDocumentSummary"/> on success. Throws — no partial upload, no Draft created
    /// — if any page's AI call fails after retries, or if the local AI endpoint is unreachable at
    /// all; see the implementation's own remarks for why this is deliberately all-or-nothing in v1.
    /// </summary>
    Task<LibraryDocumentSummary> TranslateAndSubmitAsync(
        Guid sourceLibraryFileId,
        string sourceTitle,
        string sourceFolderPath,
        string targetLanguage,
        IProgress<double>? onProgress = null,
        IProgress<string>? onStatusText = null,
        CancellationToken ct = default);

    /// <summary>Lightweight reachability/config check (<c>GET {baseUrl}/v1/models</c>) for the Settings card's "Testovat připojení" button — same endpoint the real translation preflights before doing any page work.</summary>
    Task PingAsync(CancellationToken ct = default);
}
