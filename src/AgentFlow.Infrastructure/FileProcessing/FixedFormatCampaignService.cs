using System.Globalization;
using System.Text;
using System.Text.Json;
using AgentFlow.Application.Modules.Campaigns;
using ClosedXML.Excel;

namespace AgentFlow.Infrastructure.FileProcessing;

/// <summary>
/// Implementación de IFixedFormatCampaignService.
///
/// Columnas requeridas (case-insensitive):
///   NombreCliente | Celular | CodigoPais | KeyValue
///
/// Columnas adicionales variables: cualquier otra columna presente en el Excel.
///
/// Consolidación:
///   Un único CampaignContact por número E.164 (+{CodigoPais}{Celular}).
///   Si el mismo número aparece en varias filas (múltiples pólizas),
///   todas las filas se almacenan como array "registros" en ContactDataJson.
///   Cada registro incluye NombreCliente, KeyValue y todas las columnas extra.
/// </summary>
public class FixedFormatCampaignService : IFixedFormatCampaignService
{
    // Nombres canónicos de las columnas requeridas (se buscan case-insensitive)
    private static readonly string[] RequiredColumns = ["NombreCliente", "Celular", "CodigoPais", "KeyValue"];

    public FixedFormatParseResult Parse(Stream fileStream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".csv"
            ? ParseCsv(fileStream)
            : ParseExcel(fileStream);
    }

    // ── Excel ────────────────────────────────────────────────────────────────

    private static FixedFormatParseResult ParseExcel(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var range = ws.RangeUsed();
        if (range is null)
            return new FixedFormatParseResult([], ["El archivo Excel no tiene datos."], 0, []);

        var lastRow = range.LastRow().RowNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        // Leer cabecera (fila 1)
        var headers = new List<string>();
        for (var col = 1; col <= lastCol; col++)
            headers.Add(ws.Cell(1, col).GetString().Trim());

        // Leer filas de datos
        var rows = new List<Dictionary<string, string>>();
        for (var row = 2; row <= lastRow; row++)
        {
            var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var col = 0; col < headers.Count; col++)
                rowData[headers[col]] = ws.Cell(row, col + 1).GetString().Trim();
            rows.Add(rowData);
        }

        return BuildResult(headers, rows);
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    private static FixedFormatParseResult ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is not null) lines.Add(line);
        }

        if (lines.Count == 0)
            return new FixedFormatParseResult([], ["El archivo CSV está vacío."], 0, []);

        var headers = SplitCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Count; i++)
        {
            var values = SplitCsvLine(lines[i]);
            var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var col = 0; col < headers.Count && col < values.Count; col++)
                rowData[headers[col]] = values[col].Trim();
            rows.Add(rowData);
        }

        return BuildResult(headers, rows);
    }

    private static List<string> SplitCsvLine(string line)
    {
        // Soporte básico de campos entre comillas
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }

    // ── Lógica principal de consolidación ────────────────────────────────────

    private static FixedFormatParseResult BuildResult(
        List<string> headers,
        List<Dictionary<string, string>> rows)
    {
        // Mapear cabeceras a sus índices canónicos (case-insensitive)
        var headerMap = headers
            .Select((h, i) => (Header: h, Index: i))
            .ToDictionary(x => x.Header, x => x.Index, StringComparer.OrdinalIgnoreCase);

        // Verificar columnas requeridas
        var missingCols = RequiredColumns.Where(r => !headerMap.ContainsKey(r)).ToList();
        if (missingCols.Count > 0)
            return new FixedFormatParseResult(
                [],
                [$"Columnas requeridas faltantes: {string.Join(", ", missingCols)}"],
                rows.Count,
                []);

        // Columnas extra = todas las que no son las 4 requeridas ni están vacías
        var extraColumns = headers
            .Where(h => !string.IsNullOrWhiteSpace(h)
                        && !RequiredColumns.Contains(h, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var warnings = new List<string>();
        // key = teléfono E.164, value = lista de registros de ese teléfono
        var grouped = new Dictionary<string, (List<Dictionary<string, object>> Registros, string NombreCliente)>(
            StringComparer.OrdinalIgnoreCase);

        var rowNum = 1; // 1-based para mensajes (fila 1 = primera fila de datos)
        foreach (var row in rows)
        {
            rowNum++;
            var celular = row.GetValueOrDefault("Celular", "").Trim();
            var codigoPais = row.GetValueOrDefault("CodigoPais", "").Trim();

            if (string.IsNullOrWhiteSpace(celular) || string.IsNullOrWhiteSpace(codigoPais))
            {
                warnings.Add($"Fila {rowNum}: Celular o CodigoPais vacío — omitida.");
                continue;
            }

            // Limpiar dígitos del código de país (quitar +, guiones, espacios)
            var codigoPaisDigits = new string(codigoPais.Where(char.IsDigit).ToArray());
            // Limpiar dígitos del celular
            var celularDigits = new string(celular.Where(char.IsDigit).ToArray());

            if (string.IsNullOrEmpty(codigoPaisDigits) || string.IsNullOrEmpty(celularDigits))
            {
                warnings.Add($"Fila {rowNum}: CodigoPais='{codigoPais}' o Celular='{celular}' no contienen dígitos — omitida.");
                continue;
            }

            var phone = $"+{codigoPaisDigits}{celularDigits}";

            // Validación básica E.164: al menos 7 dígitos de suscriptor + código de país (min 1 dígito)
            if (phone.Length < 9 || phone.Length > 16)
            {
                warnings.Add($"Fila {rowNum}: Teléfono '{phone}' fuera de rango E.164 — omitida.");
                continue;
            }

            var nombreCliente = row.GetValueOrDefault("NombreCliente", "").Trim();
            var keyValue = row.GetValueOrDefault("KeyValue", "").Trim();

            // Construir el registro de esta fila
            var registro = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["NombreCliente"] = nombreCliente,
                ["KeyValue"] = keyValue,
            };
            foreach (var col in extraColumns)
            {
                var val = row.GetValueOrDefault(col, "").Trim();
                if (!string.IsNullOrEmpty(val))
                    registro[col] = val;
            }

            if (!grouped.TryGetValue(phone, out var existing))
            {
                grouped[phone] = ([registro], nombreCliente);
            }
            else
            {
                existing.Registros.Add(registro);
                // Usar el nombre del primer registro como nombre del contacto
            }
        }

        var contacts = new List<ContactRow>();
        foreach (var (phone, (registros, nombreCliente)) in grouped)
        {
            var contactDataJson = JsonSerializer.Serialize(registros, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = null // preservar nombres de columna tal cual
            });

            contacts.Add(new ContactRow(
                PhoneNumber: phone,
                ClientName: nombreCliente,
                Email: null,
                PolicyNumber: null,
                InsuranceCompany: null,
                PendingAmount: null,
                Extra: null,
                ContactDataJson: contactDataJson,
                IsAlreadyE164: true
            ));
        }

        return new FixedFormatParseResult(contacts, warnings, rows.Count, extraColumns);
    }
}
