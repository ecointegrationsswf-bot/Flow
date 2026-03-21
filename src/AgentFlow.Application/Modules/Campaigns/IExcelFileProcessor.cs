namespace AgentFlow.Application.Modules.Campaigns;

public interface IExcelFileProcessor
{
    ExcelParseResult ParseExcel(Stream fileStream, string fileName);
}

public record ExcelParseResult(
    List<string> DetectedColumns,
    List<Dictionary<string, string>> PreviewRows,
    int TotalRows
);
