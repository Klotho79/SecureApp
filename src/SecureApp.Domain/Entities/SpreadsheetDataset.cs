using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>
/// Parsed-metadata record for one sheet of a spreadsheet (XLSX/CSV) that was
/// imported as a <see cref="Document"/>. Row-level data is streamed on demand
/// by <c>ISpreadsheetParsingService</c> rather than duplicated here.
/// </summary>
public sealed class SpreadsheetDataset : Entity
{
    public Guid DocumentId { get; private set; }
    public string SheetName { get; private set; }
    public int RowCount { get; private set; }
    public int ColumnCount { get; private set; }

    private SpreadsheetDataset()
    {
        SheetName = string.Empty;
    }

    public SpreadsheetDataset(Guid documentId, string sheetName, int rowCount, int columnCount)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("A dataset must belong to a document.", nameof(documentId));
        if (string.IsNullOrWhiteSpace(sheetName))
            throw new ArgumentException("Sheet name cannot be empty.", nameof(sheetName));
        if (rowCount < 0)
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (columnCount < 0)
            throw new ArgumentOutOfRangeException(nameof(columnCount));

        DocumentId = documentId;
        SheetName = sheetName;
        RowCount = rowCount;
        ColumnCount = columnCount;
    }
}
