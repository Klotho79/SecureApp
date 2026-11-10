using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.HomeAi;

/// <summary>
/// Pulls plain text out of a file the user attaches to the home-AI chat (2026-11-10): text-like files, PDF with a text layer
/// (no OCR) and Word .docx. Runs on the phone; the text is kept in memory only and goes nowhere but the home server.
/// </summary>
internal static class DocumentTextExtractor
{
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const int MaxChars = 300_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".log", ".html", ".htm", ".yaml", ".yml", ".ini", ".cfg", ".sql", ".cs", ".py", ".js", ".ts",
    };

    public static async Task<(string Text, bool Truncated)> ExtractAsync(FileResult file, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(file.FileName);
        await using var source = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxFileBytes)
            throw new InvalidOperationException("Soubor je příliš velký (nejvíc 25 MB).");

        buffer.Position = 0;
        var raw = await Task.Run(() => extension.ToLowerInvariant() switch
        {
            ".pdf" => ExtractPdf(buffer),
            ".docx" => ExtractDocx(buffer),
            ".xls" or ".xlsx" => ExtractSpreadsheet(buffer),
            _ when TextExtensions.Contains(extension) => ReadText(buffer),
            _ => throw new InvalidOperationException($"Typ souboru „{extension}“ zatím neumím přečíst. Podporuji text (.txt, .md, .csv, .json …), PDF s textem, Word (.docx) a Excel (.xls, .xlsx)."),
        }, ct);

        var text = Normalize(raw);
        if (text.Length == 0)
            throw new InvalidOperationException("V souboru jsem nenašel žádný text (u naskenovaného PDF bez textové vrstvy ho nepřečtu).");

        return text.Length > MaxChars ? (text[..MaxChars], true) : (text, false);
    }

    private static string ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static string ExtractPdf(Stream stream)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(stream);
        var text = new StringBuilder();
        for (var i = 1; i <= pdf.NumberOfPages; i++)
        {
            text.AppendLine(pdf.GetPage(i).Text);
            text.AppendLine();
        }

        return text.ToString();
    }

    private static string ExtractDocx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return string.Empty;

        var text = new StringBuilder();
        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            text.AppendLine(paragraph.InnerText);
        return text.ToString();
    }

    /// <summary>Every sheet as "## List: name" followed by its non-empty rows, cells separated by " | " (the old .xls format needs code-page encodings registered first).</summary>
    private static string ExtractSpreadsheet(Stream stream)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
        var text = new StringBuilder();
        do
        {
            text.Append("## List: ").AppendLine(reader.Name);
            while (reader.Read())
            {
                var cells = new List<string>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                    cells.Add(CellText(reader.GetValue(i)));

                // Trailing empty cells carry nothing; a fully empty row is skipped.
                while (cells.Count > 0 && cells[^1].Length == 0)
                    cells.RemoveAt(cells.Count - 1);
                if (cells.Count > 0)
                    text.AppendLine(string.Join(" | ", cells));

                if (text.Length > MaxChars * 2)
                    return text.ToString(); // enough: the caller truncates to MaxChars anyway
            }

            text.AppendLine();
        }
        while (reader.NextResult());

        return text.ToString();
    }

    private static string CellText(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.TimeOfDay == TimeSpan.Zero ? date.ToString("d. M. yyyy", System.Globalization.CultureInfo.InvariantCulture) : date.ToString("d. M. yyyy H:mm", System.Globalization.CultureInfo.InvariantCulture),
        double number => number.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
        bool flag => flag ? "ano" : "ne",
        _ => value.ToString()?.Trim() ?? string.Empty,
    };

    private static string Normalize(string raw)
    {
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\0', ' ');
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
