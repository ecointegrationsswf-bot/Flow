using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Representa un ítem (fila) extraído del payload de respuesta del webhook de morosidad.
/// Los campos tipados (PhoneNormalized, ClientName, Amount, PolicyNumber) se extraen
/// vía ActionFieldMapping/JsonPath. RawData preserva el objeto JSON original.
/// </summary>
public class DelinquencyItem
{
    public Guid Id { get; set; }

    public Guid ExecutionId { get; set; }
    public DelinquencyExecution? Execution { get; set; }

    /// <summary>Posición del ítem en el array original (0-based). Útil para trazar errores.</summary>
    public int RowIndex { get; set; }

    /// <summary>Teléfono tal como viene en el payload, antes de normalizar.</summary>
    public string? PhoneRaw { get; set; }

    /// <summary>Teléfono normalizado a E.164 con código de país. Null si no pudo normalizarse.</summary>
    public string? PhoneNormalized { get; set; }

    public string? ClientName { get; set; }
    public string? PolicyNumber { get; set; }
    public decimal? Amount { get; set; }

    /// <summary>
    /// Referencia externa del registro extraída del campo con Role=KeyValue.
    /// La etiqueta semántica ("Número de póliza" / "Cédula") vive en el ActionFieldMapping del rol —
    /// aquí guardamos sólo el valor para preservar la trazabilidad por ítem.
    /// </summary>
    public string? KeyValue { get; set; }

    /// <summary>JSON del objeto original de este ítem tal como vino del webhook.</summary>
    public string? RawData { get; set; }

    /// <summary>
    /// Diccionario JSON (Dict&lt;string,string?&gt;) con TODOS los campos extraídos según el mapping
    /// de la acción — incluye los semánticos y los extras. Las claves son el ColumnKey del mapping.
    /// Permite renderizar el historial con columnas dinámicas sin volver a parsear RawData.
    /// </summary>
    public string? ExtractedDataJson { get; set; }

    public DelinquencyItemStatus Status { get; set; } = DelinquencyItemStatus.Pending;

    /// <summary>Razón de descarte si Status = Discarded. Ej: "Teléfono inválido", "Duplicado".</summary>
    public string? DiscardReason { get; set; }

    public Guid? GroupId { get; set; }
    public ContactGroup? Group { get; set; }
}
