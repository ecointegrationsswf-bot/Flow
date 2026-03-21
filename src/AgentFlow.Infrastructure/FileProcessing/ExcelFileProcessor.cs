using AgentFlow.Application.Modules.Campaigns;
using ClosedXML.Excel;

namespace AgentFlow.Infrastructure.FileProcessing;

public class ExcelFileProcessor : IExcelFileProcessor
{
    public ExcelParseResult ParseExcel(Stream fileStream, string fileName)
    {
        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheets.First();
        var range = worksheet.RangeUsed();

        if (range is null)
            return new ExcelParseResult([], [], 0);

        var lastRow = range.LastRow().RowNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        // Detectar columnas del header (fila 1)
        var columns = new List<string>();
        for (var col = 1; col <= lastCol; col++)
        {
            var header = worksheet.Cell(1, col).GetString().Trim();
            if (!string.IsNullOrEmpty(header))
                columns.Add(header);
        }

        if (columns.Count == 0)
            return new ExcelParseResult([], [], 0);

        var totalRows = lastRow - 1; // excluir header

        // Preview: primeras 5 filas
        var previewRows = new List<Dictionary<string, string>>();
        var previewCount = Math.Min(5, totalRows);
        for (var row = 2; row <= 1 + previewCount; row++)
        {
            var rowData = new Dictionary<string, string>();
            for (var col = 0; col < columns.Count; col++)
            {
                rowData[columns[col]] = worksheet.Cell(row, col + 1).GetString().Trim();
            }
            previewRows.Add(rowData);
        }

        return new ExcelParseResult(columns, previewRows, totalRows);
    }
}
