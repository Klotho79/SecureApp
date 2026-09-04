using System.Text;
using PDFtoImage;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SkiaSharp;

namespace SecureApp.Presentation.Rendering;

/// <inheritdoc cref="IDocumentRenderingService"/>
/// <remarks>
/// <see cref="IDocumentRenderingService.GetPageCountAsync"/> has no viewport size parameter,
/// so page counts have to be well-defined independent of whatever <c>maxWidth</c>/<c>maxHeight</c>
/// a later <see cref="RenderPageAsync"/> call happens to ask for. Pdf and Image are fine —
/// they have real, intrinsic pages/dimensions. PlainText and Spreadsheet don't, so this class
/// lays them out once against a fixed "canonical" virtual page (<see cref="PageWidth"/> x
/// <see cref="PageHeight"/>, ~US Letter at 96 DPI) and treats <c>maxWidth</c>/<c>maxHeight</c>
/// purely as an output-resolution cap on the raster of that already-paginated content — the
/// same role it plays for Pdf/Image. <c>RenderedPage.PixelData</c> is PNG-encoded (not raw
/// pixels), so it can be handed straight to any MAUI <c>Image</c> via
/// <c>ImageSource.FromStream</c> without requiring a SkiaSharp canvas view in the UI layer.
/// </remarks>
public sealed class DocumentRenderingService : IDocumentRenderingService
{
    private const int PageWidth = 816;
    private const int PageHeight = 1056;
    private const int Margin = 48;
    private const float FontSize = 14f;
    private const float LineHeight = FontSize * 1.4f;

    private readonly IDocumentRepository _documentRepository;
    private readonly ICryptoService _crypto;
    private readonly ISpreadsheetParsingService _spreadsheetParsingService;

    public DocumentRenderingService(
        IDocumentRepository documentRepository,
        ICryptoService crypto,
        ISpreadsheetParsingService spreadsheetParsingService)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _spreadsheetParsingService = spreadsheetParsingService ?? throw new ArgumentNullException(nameof(spreadsheetParsingService));
    }

    public async Task<int> GetPageCountAsync(Guid documentId, CancellationToken ct = default)
    {
        var (document, plaintext) = await LoadAsync(documentId, ct);

        return document.DocumentType switch
        {
            DocumentType.Pdf => Conversion.GetPageCount(plaintext),
            DocumentType.Image => 1,
            DocumentType.PlainText => PaginateText(plaintext).Count,
            DocumentType.Spreadsheet => (await PaginateSpreadsheetAsync(documentId, plaintext, ct)).Count,
            _ => throw new NotSupportedException($"Rendering is not supported for document type '{document.DocumentType}'.")
        };
    }

    public async Task<RenderedPage> RenderPageAsync(Guid documentId, int pageIndex, int maxWidth, int maxHeight, CancellationToken ct = default)
    {
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (maxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (maxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maxHeight));

        var (document, plaintext) = await LoadAsync(documentId, ct);

        using var bitmap = document.DocumentType switch
        {
            DocumentType.Pdf => RenderPdfPage(plaintext, pageIndex, maxWidth, maxHeight),
            DocumentType.Image => RenderImagePage(plaintext, pageIndex, maxWidth, maxHeight),
            DocumentType.PlainText => RenderTextPage(plaintext, pageIndex, maxWidth, maxHeight),
            DocumentType.Spreadsheet => await RenderSpreadsheetPageAsync(documentId, plaintext, pageIndex, maxWidth, maxHeight, ct),
            _ => throw new NotSupportedException($"Rendering is not supported for document type '{document.DocumentType}'.")
        };

        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new RenderedPage(encoded.ToArray(), bitmap.Width, bitmap.Height);
    }

    private async Task<(Document Document, byte[] Plaintext)> LoadAsync(Guid documentId, CancellationToken ct)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, ct) ?? throw new DocumentNotFoundException(documentId);
        var plaintext = await _crypto.DecryptAsync(document.EncryptedContent, ct);
        return (document, plaintext);
    }

    // ============================== Pdf ==============================

    private static SKBitmap RenderPdfPage(byte[] pdfBytes, int pageIndex, int maxWidth, int maxHeight)
    {
        var pageCount = Conversion.GetPageCount(pdfBytes);
        if (pageIndex >= pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"Document has {pageCount} page(s).");

        // WithAspectRatio makes Width/Height an upper bound the real page is fit inside,
        // rather than a target size it gets stretched/distorted to.
        return Conversion.ToImage(pdfBytes, pageIndex, options: new RenderOptions(Width: maxWidth, Height: maxHeight, WithAspectRatio: true));
    }

    // ============================== Image ==============================

    private static SKBitmap RenderImagePage(byte[] imageBytes, int pageIndex, int maxWidth, int maxHeight)
    {
        if (pageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "An image document has exactly one page.");

        using var original = SKBitmap.Decode(imageBytes) ?? throw new UnsupportedFileFormatException($"image content ({imageBytes.Length} bytes)");
        return ScaleToFit(original, maxWidth, maxHeight);
    }

    // ============================== PlainText ==============================

    private static SKBitmap RenderTextPage(byte[] plaintext, int pageIndex, int maxWidth, int maxHeight)
    {
        var pages = PaginateText(plaintext);
        if (pageIndex >= pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"Document has {pages.Count} page(s).");

        using var canonical = new SKBitmap(PageWidth, PageHeight);
        using (var canvas = new SKCanvas(canonical))
        using (var font = new SKFont { Size = FontSize })
        using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black })
        {
            canvas.Clear(SKColors.White);
            var y = Margin + FontSize;
            foreach (var line in pages[pageIndex])
            {
                canvas.DrawText(line, Margin, y, SKTextAlign.Left, font, paint);
                y += LineHeight;
            }
        }

        return ScaleToFit(canonical, maxWidth, maxHeight);
    }

    private static List<List<string>> PaginateText(byte[] plaintext)
    {
        var text = DecodeText(plaintext);

        using var font = new SKFont { Size = FontSize };
        using var paint = new SKPaint { IsAntialias = true };
        var lines = WrapText(text, font, paint, PageWidth - 2 * Margin);

        var linesPerPage = Math.Max(1, (int)((PageHeight - 2 * Margin) / LineHeight));
        var pages = new List<List<string>>();
        for (var i = 0; i < lines.Count; i += linesPerPage)
            pages.Add(lines.Skip(i).Take(linesPerPage).ToList());

        return pages;
    }

    /// <summary>Assumes UTF-8 (stripping a leading BOM if present) — good enough for the .txt/.md/.log files this document type covers.</summary>
    private static string DecodeText(byte[] bytes)
    {
        var hasUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return hasUtf8Bom ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3) : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Greedy word-wrap using real glyph measurement (<see cref="SKFont.MeasureText(string, SKPaint)"/>), not a fixed character count.</summary>
    private static List<string> WrapText(string text, SKFont font, SKPaint paint, float maxLineWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var currentLine = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = currentLine.Length == 0 ? word : $"{currentLine} {word}";
                if (currentLine.Length == 0 || font.MeasureText(candidate, paint) <= maxLineWidth)
                {
                    currentLine.Clear().Append(candidate);
                }
                else
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear().Append(word);
                }
            }
            lines.Add(currentLine.ToString());
        }

        return lines;
    }

    // ============================== Spreadsheet ==============================

    private async Task<SKBitmap> RenderSpreadsheetPageAsync(Guid documentId, byte[] plaintext, int pageIndex, int maxWidth, int maxHeight, CancellationToken ct)
    {
        var pages = await PaginateSpreadsheetAsync(documentId, plaintext, ct);
        if (pageIndex >= pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"Document has {pages.Count} page(s).");

        var page = pages[pageIndex];
        var columnCount = Math.Max(1, page.ColumnCount);
        var columnWidth = (PageWidth - 2f * Margin) / columnCount;
        var rowHeight = LineHeight + 4;

        using var canonical = new SKBitmap(PageWidth, PageHeight);
        using (var canvas = new SKCanvas(canonical))
        using (var font = new SKFont { Size = FontSize })
        using (var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black })
        using (var gridPaint = new SKPaint { Color = SKColors.LightGray, StrokeWidth = 1, Style = SKPaintStyle.Stroke })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText(page.SheetName, Margin, Margin, SKTextAlign.Left, font, textPaint);

            var top = Margin + LineHeight + 8;
            for (var r = 0; r < page.Rows.Count; r++)
            {
                var rowTop = top + r * rowHeight;
                var row = page.Rows[r];
                for (var c = 0; c < columnCount && c < row.Count; c++)
                {
                    var cellLeft = Margin + c * columnWidth;
                    var cellRect = new SKRect(cellLeft, rowTop, cellLeft + columnWidth, rowTop + rowHeight);
                    canvas.DrawRect(cellRect, gridPaint);

                    canvas.Save();
                    canvas.ClipRect(cellRect);
                    canvas.DrawText(row[c] ?? string.Empty, cellLeft + 4, rowTop + rowHeight - 6, SKTextAlign.Left, font, textPaint);
                    canvas.Restore();
                }
            }
        }

        return ScaleToFit(canonical, maxWidth, maxHeight);
    }

    /// <summary>One page-list entry per printed page; sheets never share a page, each starts fresh.</summary>
    private async Task<List<SpreadsheetPageContent>> PaginateSpreadsheetAsync(Guid documentId, byte[] plaintext, CancellationToken ct)
    {
        var datasets = await _spreadsheetParsingService.ParseWorkbookAsync(documentId, new MemoryStream(plaintext), ct);

        var rowHeight = LineHeight + 4;
        var rowsPerPage = Math.Max(1, (int)((PageHeight - Margin - (Margin + LineHeight + 8)) / rowHeight));

        var pages = new List<SpreadsheetPageContent>();
        foreach (var dataset in datasets)
        {
            var rows = await _spreadsheetParsingService.ReadRowsAsync(documentId, dataset.SheetName, new MemoryStream(plaintext), ct);
            if (rows.Count == 0)
            {
                pages.Add(new SpreadsheetPageContent(dataset.SheetName, dataset.ColumnCount, []));
                continue;
            }

            for (var i = 0; i < rows.Count; i += rowsPerPage)
                pages.Add(new SpreadsheetPageContent(dataset.SheetName, dataset.ColumnCount, [.. rows.Skip(i).Take(rowsPerPage)]));
        }

        if (pages.Count == 0)
            pages.Add(new SpreadsheetPageContent(string.Empty, 0, []));

        return pages;
    }

    private readonly record struct SpreadsheetPageContent(string SheetName, int ColumnCount, IReadOnlyList<IReadOnlyList<string?>> Rows);

    // ============================== Shared ==============================

    /// <summary>Fits <paramref name="source"/> within maxWidth x maxHeight, preserving aspect ratio, never upscaling. Always returns a fresh bitmap independent of <paramref name="source"/>'s lifetime.</summary>
    private static SKBitmap ScaleToFit(SKBitmap source, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1f, Math.Min((float)maxWidth / source.Width, (float)maxHeight / source.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

        if (targetWidth == source.Width && targetHeight == source.Height)
        {
            var copy = new SKBitmap(source.Info);
            source.CopyTo(copy);
            return copy;
        }

        return source.Resize(new SKImageInfo(targetWidth, targetHeight), SKSamplingOptions.Default)
            ?? throw new InvalidOperationException("Failed to resize a rendered page.");
    }
}
