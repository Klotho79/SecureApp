using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SecureApp.Domain.ValueObjects;
// "Cell" is ambiguous with Microsoft.Maui.Controls.Cell (a TableView row element), which this file's
// implicit MAUI global usings also bring in — alias to the actual type this file means.
using Cell = DocumentFormat.OpenXml.Spreadsheet.Cell;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Parses the picc_team member's own uploaded roster xlsx (2026-10-12) — see
/// <c>IPiccRosterService</c>'s own remarks on why this is a manual upload rather than a live
/// Google Sheets sync. The exact column layout was deliberately NOT nailed down before building
/// this (user's own call: "Nejdřív vyřeš přístup, formát pak") — this detects columns by HEADER
/// TEXT instead of a fixed position, so it tolerates the sheet being reordered or having extra
/// columns, and degrades to the first two columns if no header text matches at all rather than
/// refusing to parse. If a real file turns out to need a different header word, add it to
/// <see cref="DateHeaderWords"/>/<see cref="DoctorHeaderWords"/>/<see cref="NurseHeaderWords"/> —
/// nothing else here should need to change.
///
/// Nurses (2026-10-12, user's own clarification: "Sestry maji dvoji format mohou byt samy nebo ve
/// dvou a jmena oddelena plus nebo carkou") — the nurse cell is split on '+' or ',', trimmed, empty
/// pieces dropped, so it yields 0, 1 or 2 names.
/// </summary>
public static class PiccRosterParser
{
    private static readonly string[] DateHeaderWords = ["datum", "den"];
    private static readonly string[] DoctorHeaderWords = ["lekar", "doktor"];
    private static readonly string[] NurseHeaderWords = ["sestr"];
    private static readonly CultureInfo Czech = new("cs-CZ");
    private static readonly string[] DateFormats = ["d.M.yyyy", "d. M. yyyy", "dd.MM.yyyy", "d.M.yy", "yyyy-MM-dd"];

    /// <summary>Finds the sheet whose name looks like "Lékaři" (falls back to the first sheet) and returns everything it could read for every day in it, plus the one row matching <paramref name="today"/> if any.</summary>
    public static PiccRosterSnapshot Parse(Stream xlsxContent, DateOnly today)
    {
        using var document = SpreadsheetDocument.Open(xlsxContent, isEditable: false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Soubor nemá žádný list (chybí workbook).");
        var sharedStrings = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault()?.SharedStringTable;

        var sheets = workbookPart.Workbook.Descendants<Sheet>().ToList();
        if (sheets.Count == 0)
            throw new InvalidOperationException("Soubor neobsahuje žádný list.");

        var doctorsSheet = sheets.FirstOrDefault(s => Normalize(s.Name?.Value ?? string.Empty).Contains("lekar")) ?? sheets[0];
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(doctorsSheet.Id!.Value!);
        var rows = worksheetPart.Worksheet.Descendants<Row>().ToList();
        if (rows.Count < 2)
            throw new InvalidOperationException($"List „{doctorsSheet.Name}“ nemá žádné datové řádky (jen záhlaví nebo je prázdný).");

        var header = RowCellsByColumn(rows[0], sharedStrings);
        var dateCol = FindColumn(header, DateHeaderWords) ?? 0;
        var doctorCol = FindColumn(header, DoctorHeaderWords) ?? 1;
        var nurseCol = FindColumn(header, NurseHeaderWords); // optional — stays null if no such column

        var days = new List<PiccRosterDay>();
        foreach (var row in rows.Skip(1))
        {
            var cells = RowCellsByColumn(row, sharedStrings);
            if (!cells.TryGetValue(dateCol, out var dateText) || !TryParseDate(dateText, out var date))
                continue;

            var doctorName = cells.GetValueOrDefault(doctorCol)?.Trim();
            var nurseNames = nurseCol is { } nc ? SplitNurses(cells.GetValueOrDefault(nc)) : [];
            days.Add(new PiccRosterDay(date, string.IsNullOrWhiteSpace(doctorName) ? null : doctorName, nurseNames));
        }

        if (days.Count == 0)
            throw new InvalidOperationException($"List „{doctorsSheet.Name}“: v žádném řádku se nepodařilo rozpoznat datum (sloupec „{Column(header, dateCol)}“) — zkontrolujte formát data.");

        var todayEntry = days.FirstOrDefault(d => d.Date == today);
        return new PiccRosterSnapshot(todayEntry, days, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<string> SplitNurses(string? cellValue)
    {
        if (string.IsNullOrWhiteSpace(cellValue)) return [];
        return cellValue.Split(['+', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static int? FindColumn(IReadOnlyDictionary<int, string> header, string[] words)
    {
        foreach (var (index, text) in header)
        {
            var normalized = Normalize(text);
            if (words.Any(normalized.Contains))
                return index;
        }
        return null;
    }

    private static string Column(IReadOnlyDictionary<int, string> header, int index) => header.GetValueOrDefault(index, $"#{index + 1}");

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        // Excel stores a real date cell as an OLE Automation serial number (days since 1899-12-30),
        // not a formatted string — this is the common case for a proper date column.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            try { date = DateOnly.FromDateTime(DateTime.FromOADate(serial)); return true; }
            catch (ArgumentException) { return false; }
        }

        if (DateTime.TryParseExact(text, DateFormats, Czech, DateTimeStyles.None, out var exact))
        {
            date = DateOnly.FromDateTime(exact);
            return true;
        }
        if (DateTime.TryParse(text, Czech, DateTimeStyles.None, out var loose))
        {
            date = DateOnly.FromDateTime(loose);
            return true;
        }
        return false;
    }

    /// <summary>Maps every cell in a row to its real column index (from the "B3"-style CellReference), not its position in the XML — a row with blank/omitted cells would otherwise shift every later column left.</summary>
    private static Dictionary<int, string> RowCellsByColumn(Row row, SharedStringTable? sharedStrings)
    {
        var result = new Dictionary<int, string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var column = ColumnIndexFromReference(cell.CellReference?.Value);
            if (column < 0) continue;
            result[column] = CellText(cell, sharedStrings);
        }
        return result;
    }

    private static string CellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var raw = cell.CellValue?.InnerText ?? cell.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (sharedStrings is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < sharedStrings.ChildElements.Count)
                return sharedStrings.ChildElements[index].InnerText;
            return raw;
        }
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? raw;
        return raw;
    }

    private static int ColumnIndexFromReference(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference)) return -1;
        var index = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch)) break; // stops at the row-number digits
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return index - 1; // zero-based
    }

    /// <summary>Diacritics/case-insensitive header match ("Lékaři" ~ "lekari") — deliberately simpler than OpicentrumParsing.NormalizeName (no title-stripping needed for a column header).</summary>
    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }
}
