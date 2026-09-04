using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Data.Import;

/// <inheritdoc cref="ISpreadsheetParsingService"/>
public sealed class ExcelDataReaderSpreadsheetParsingService : ISpreadsheetParsingService
{
    static ExcelDataReaderSpreadsheetParsingService()
    {
        // Needed for legacy .xls (BIFF/OLE2) workbooks using code-page text encodings that
        // .NET no longer registers by default; harmless no-op for XLSX/OOXML and CSV.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Task<IReadOnlyList<SpreadsheetDataset>> ParseWorkbookAsync(Guid documentId, Stream fileStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ct.ThrowIfCancellationRequested();

        using var reader = CreateReader(fileStream, documentId);
        using var dataSet = reader.AsDataSet();

        var datasets = new List<SpreadsheetDataset>(dataSet.Tables.Count);
        foreach (DataTable table in dataSet.Tables)
            datasets.Add(new SpreadsheetDataset(documentId, table.TableName, table.Rows.Count, table.Columns.Count));

        return Task.FromResult<IReadOnlyList<SpreadsheetDataset>>(datasets);
    }

    public Task<IReadOnlyList<IReadOnlyList<string?>>> ReadRowsAsync(Guid documentId, string sheetName, Stream fileStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ct.ThrowIfCancellationRequested();

        using var reader = CreateReader(fileStream, documentId);
        using var dataSet = reader.AsDataSet();

        var table = dataSet.Tables[sheetName]
            ?? throw new InvalidOperationException($"Sheet '{sheetName}' was not found in document '{documentId}'.");

        var rows = new List<IReadOnlyList<string?>>(table.Rows.Count);
        foreach (DataRow row in table.Rows)
        {
            var cells = new string?[table.Columns.Count];
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var value = row[i];
                cells[i] = value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            rows.Add(cells);
        }

        return Task.FromResult<IReadOnlyList<IReadOnlyList<string?>>>(rows);
    }

    /// <summary>
    /// There's no filename here (by design — the interface only ever sees already-decrypted
    /// bytes), so format is auto-detected from content: try XLS/XLSX first (ExcelReaderFactory
    /// sniffs the OLE2/ZIP header itself and throws if neither matches), then fall back to CSV.
    /// </summary>
    private static IExcelDataReader CreateReader(Stream stream, Guid documentId)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("The spreadsheet source stream must be seekable.", nameof(stream));

        stream.Position = 0;
        try
        {
            return ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration());
        }
        catch (Exception)
        {
            stream.Position = 0;
            try
            {
                return ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration());
            }
            catch (Exception)
            {
                throw new UnsupportedFileFormatException(documentId.ToString());
            }
        }
    }
}
