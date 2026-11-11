using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.HomeAi;

/// <summary>The text of an attached file, whether it had to be cut, and (Excel only) the sheets as tables for exact calculations.</summary>
internal sealed record ExtractedDocument(string Text, bool Truncated, IReadOnlyList<SpreadsheetTable> Tables);

/// <summary>One sheet: column names and its non-empty data rows (with their row numbers in the file).</summary>
internal sealed record SpreadsheetTable(string Name, string[] Columns, List<(int Number, object?[] Values)> Rows);

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

    public static async Task<ExtractedDocument> ExtractAsync(FileResult file, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(file.FileName);
        await using var source = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxFileBytes)
            throw new InvalidOperationException("Soubor je příliš velký (nejvíc 25 MB).");

        buffer.Position = 0;
        var tables = new List<SpreadsheetTable>();
        var raw = await Task.Run(() => extension.ToLowerInvariant() switch
        {
            ".pdf" => ExtractPdf(buffer),
            ".docx" => ExtractDocx(buffer),
            ".xls" or ".xlsx" => ExtractSpreadsheet(buffer, tables),
            _ when TextExtensions.Contains(extension) => ReadText(buffer),
            _ => throw new InvalidOperationException($"Typ souboru „{extension}“ zatím neumím přečíst. Podporuji text (.txt, .md, .csv, .json …), PDF s textem, Word (.docx) a Excel (.xls, .xlsx)."),
        }, ct);

        var text = Normalize(raw);
        if (text.Length == 0)
            throw new InvalidOperationException("V souboru jsem nenašel žádný text (u naskenovaného PDF bez textové vrstvy ho nepřečtu).");

        return text.Length > MaxChars ? new(text[..MaxChars], true, tables) : new(text, false, tables);
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

    /// <summary>
    /// Every sheet laid out so that a small model can follow it (2026-11-10): a "## List:" line, a "Sloupce:" line naming the
    /// columns (header row or letters A, B, C…), a "Souhrn čísel:" line with count/sum/average/min/max of every numeric column
    /// computed here in code (a model cannot add up hundreds of cells reliably), then the rows as "Ř&lt;number&gt;: a | b | c" with
    /// empty cells kept so each value stays under its column. DocumentReader repeats the first three lines in every piece.
    /// The old .xls format needs code-page encodings registered first.
    /// </summary>
    private static string ExtractSpreadsheet(Stream stream, List<SpreadsheetTable> tables)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
        var text = new StringBuilder();
        do
        {
            var rows = new List<(int Number, object?[] Values)>();
            var number = 0;
            while (reader.Read())
            {
                number++;
                var values = new object?[reader.FieldCount];
                var any = false;
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = reader.GetValue(i);
                    if (!any && CellText(values[i]).Length > 0)
                        any = true;
                }

                if (any)
                    rows.Add((number, values));
                if (rows.Count >= 100_000)
                    break;
            }

            // Past the text limit the sheet is still read into a table (the exact calculations see all rows), just not written out.
            var table = AppendSheet(text.Length > MaxChars * 2 ? new StringBuilder() : text, reader.Name, rows);
            if (table is not null)
                tables.Add(table);
        }
        while (reader.NextResult());

        return text.ToString();
    }

    private static SpreadsheetTable? AppendSheet(StringBuilder text, string sheetName, List<(int Number, object?[] Values)> rows)
    {
        if (rows.Count == 0)
        {
            text.Append("## List: ").Append(sheetName).AppendLine(" (prázdný)").AppendLine();
            return null;
        }

        var columns = rows.Max(r => r.Values.Length);
        var first = rows[0].Values;
        var hasHeader = first.Any(v => v is string { Length: > 0 }) && !first.Any(v => v is double or DateTime);
        var names = new string[columns];
        for (var c = 0; c < columns; c++)
        {
            var header = hasHeader && c < first.Length ? CellText(first[c]) : string.Empty;
            names[c] = header.Length > 0 ? header : ColumnLetter(c);
        }

        var data = hasHeader ? rows.Skip(1).ToList() : rows;
        text.Append("## List: ").Append(sheetName).Append(" (").Append(data.Count).Append(" řádků, ").Append(columns).AppendLine(" sloupců)");
        text.Append("Sloupce: ").AppendLine(string.Join(" | ", names.Select((n, c) => hasHeader && n != ColumnLetter(c) ? $"{ColumnLetter(c)}={n}" : n)));

        var summary = new List<string>();
        var numeric = new bool[columns];
        var filledCount = new int[columns];
        for (var c = 0; c < columns; c++)
        {
            var numbers = new List<double>();
            foreach (var (_, values) in data)
            {
                if (c >= values.Length || CellText(values[c]).Length == 0)
                    continue;
                filledCount[c]++;
                if (values[c] is double d)
                    numbers.Add(d);
            }

            if (numbers.Count >= 3 && numbers.Count >= filledCount[c] * 0.6)
            {
                numeric[c] = true;
                if (summary.Count < 12)
                {
                    var sum = numbers.Sum();
                    summary.Add($"{names[c]}: počet {numbers.Count}, součet {Num(sum)}, průměr {Num(sum / numbers.Count)}, min {Num(numbers.Min())}, max {Num(numbers.Max())}");
                }
            }
        }

        if (summary.Count > 0)
            text.Append("Souhrn čísel (přesně spočítáno programem, použij tato čísla místo vlastního počítání): ").AppendLine(string.Join("; ", summary));

        AppendFrequencies(text, data, names, numeric, filledCount);

        foreach (var (rowNumber, values) in data)
        {
            var cells = values.Select(CellText).ToList();
            // Trailing empty cells carry nothing; empty cells in the middle stay so the columns line up.
            while (cells.Count > 0 && cells[^1].Length == 0)
                cells.RemoveAt(cells.Count - 1);
            text.Append("Ř").Append(rowNumber).Append(": ").AppendLine(string.Join(" | ", cells));
        }

        text.AppendLine();
        return new SpreadsheetTable(sheetName, names, data);
    }

    /// <summary>
    /// Exact counts for text columns with few distinct values ("Četnosti …": how many rows per diagnosis / procedure / department…)
    /// and, for a count column (name contains "poč"), its sum per value of a diagnosis / procedure column ("Součet … podle …").
    /// A model cannot tally hundreds of rows reliably, so the program does it.
    /// </summary>
    private static void AppendFrequencies(StringBuilder text, List<(int Number, object?[] Values)> data, string[] names, bool[] numeric, int[] filledCount)
    {
        static bool Priority(string name) =>
            name.Contains("diagn", StringComparison.OrdinalIgnoreCase)
            || name.Contains("výkon", StringComparison.OrdinalIgnoreCase)
            || name.Contains("vykon", StringComparison.OrdinalIgnoreCase);

        var categorical = new List<int>();
        for (var c = 0; c < names.Length; c++)
        {
            if (numeric[c] || filledCount[c] < 3)
                continue;
            var distinct = data.Where(r => c < r.Values.Length).Select(r => CellText(r.Values[c])).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var limit = Priority(names[c]) ? 150 : 40;
            if (distinct >= 2 && distinct <= limit && distinct <= filledCount[c] * 0.7)
                categorical.Add(c);
        }

        // Diagnosis / procedure columns first, then the rest; at most 8 columns.
        foreach (var c in categorical.OrderByDescending(c => Priority(names[c])).Take(8))
        {
            var groups = data.Where(r => c < r.Values.Length && CellText(r.Values[c]).Length > 0)
                .GroupBy(r => CellText(r.Values[c]), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
            var line = string.Join("; ", groups.Select(g => $"{g.Key} ({g.Count()})"));
            text.Append("Četnosti „").Append(names[c]).Append("“ (").Append(groups.Count).Append(" různých hodnot; přesně spočítáno): ")
                .AppendLine(Shorten(line, Priority(names[c]) ? 2500 : 700));
        }

        var counts = Enumerable.Range(0, names.Length).Where(c => numeric[c] && (names[c].Contains("poč", StringComparison.OrdinalIgnoreCase) || names[c].Contains("poc", StringComparison.OrdinalIgnoreCase))).Take(2).ToList();
        var by = categorical.Where(c => Priority(names[c])).Take(2).ToList();
        foreach (var n in counts)
        {
            foreach (var g in by)
            {
                var sums = data.Where(r => g < r.Values.Length && n < r.Values.Length && CellText(r.Values[g]).Length > 0 && r.Values[n] is double)
                    .GroupBy(r => CellText(r.Values[g]), StringComparer.OrdinalIgnoreCase)
                    .Select(x => (Key: x.Key, Sum: x.Sum(r => (double)r.Values[n]!)))
                    .OrderByDescending(x => x.Sum).ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (sums.Count == 0)
                    continue;
                text.Append("Součet „").Append(names[n]).Append("“ podle „").Append(names[g]).Append("“ (přesně spočítáno): ")
                    .AppendLine(Shorten(string.Join("; ", sums.Select(x => $"{x.Key} = {Num(x.Sum)}")), 2500));
            }
        }
    }

    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max] + " … (zkráceno)";

    private static string Num(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string ColumnLetter(int index)
    {
        var letters = string.Empty;
        for (var n = index; n >= 0; n = n / 26 - 1)
            letters = (char)('A' + n % 26) + letters;
        return letters;
    }

    internal static string CellText(object? value) => value switch
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
