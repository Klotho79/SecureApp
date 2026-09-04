using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>Parses XLSX/CSV content (via ExcelDataReader in the Data layer) into tabular data.</summary>
public interface ISpreadsheetParsingService
{
    Task<IReadOnlyList<SpreadsheetDataset>> ParseWorkbookAsync(Guid documentId, Stream fileStream, CancellationToken ct = default);

    Task<IReadOnlyList<IReadOnlyList<string?>>> ReadRowsAsync(Guid documentId, string sheetName, Stream fileStream, CancellationToken ct = default);
}
