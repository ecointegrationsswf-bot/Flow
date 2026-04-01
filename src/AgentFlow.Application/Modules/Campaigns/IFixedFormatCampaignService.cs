namespace AgentFlow.Application.Modules.Campaigns;

/// <summary>
/// Resultado del parseo de un Excel en formato fijo.
/// </summary>
public record FixedFormatParseResult(
    /// <summary>Contactos consolidados listos para StartCampaignCommand.</summary>
    List<ContactRow> Contacts,
    /// <summary>Filas omitidas por datos inválidos (teléfono vacío, país inválido, etc.).</summary>
    List<string> Warnings,
    /// <summary>Total de filas de datos leídas del archivo (sin cabecera).</summary>
    int TotalRowsRead,
    /// <summary>Columnas variables detectadas además de las 4 requeridas.</summary>
    List<string> ExtraColumns
);

/// <summary>
/// Parsea archivos Excel en formato fijo con columnas requeridas:
/// NombreCliente | Celular | CodigoPais | KeyValue
/// más columnas variables adicionales.
///
/// Consolida múltiples filas del mismo número de teléfono en un único
/// CampaignContact cuyo ContactDataJson contiene un array de registros.
/// </summary>
public interface IFixedFormatCampaignService
{
    /// <summary>
    /// Parsea el stream (Excel .xlsx/.xls o CSV) y retorna los contactos consolidados.
    /// </summary>
    FixedFormatParseResult Parse(Stream fileStream, string fileName);
}
