namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Un campo de entrada del payload del webhook.
/// Describe de dónde viene el valor y cómo se mapea en el request final.
/// </summary>
public class InputField
{
    /// <summary>
    /// Dot-notation: 'campo' (flat) o 'obj.campo' (nested).
    /// Ej: "cliente.cedula", "pago.monto", "meta.canal"
    /// </summary>
    public string FieldPath { get; set; } = string.Empty;

    /// <summary>system | conversation | static</summary>
    public string SourceType { get; set; } = "system";

    /// <summary>
    /// Path en el sistema cuando SourceType = system (ej: "contact.idNumber", "session.id").
    /// Nombre del param recolectado cuando SourceType = conversation (ej: "amount").
    /// </summary>
    public string? SourceKey { get; set; }

    /// <summary>Valor fijo cuando SourceType = static.</summary>
    public string? StaticValue { get; set; }

    /// <summary>string | number | boolean | date | array</summary>
    public string DataType { get; set; } = "string";

    public bool Required { get; set; }

    /// <summary>Valor por defecto si no existe en la fuente (solo cuando Required=false).</summary>
    public string? DefaultValue { get; set; }
}
