namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Un campo de la respuesta del webhook y su acción de procesamiento.
/// </summary>
public class OutputField
{
    /// <summary>Dot-notation en el JSON de respuesta del tenant. Ej: "link_pago", "documento.archivo".</summary>
    public string FieldPath { get; set; } = string.Empty;

    /// <summary>string | number | boolean | date | url | base64 | array | object</summary>
    public string DataType { get; set; } = "string";

    /// <summary>Solo cuando DataType = base64. Ej: "application/pdf", "image/jpeg".</summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Qué hace el sistema con este campo:
    /// send_to_agent | send_whatsapp_media | inject_context | log_only | trigger_escalation
    /// </summary>
    public string OutputAction { get; set; } = "send_to_agent";

    /// <summary>Etiqueta legible para el agente IA y para logs.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Si es false, la ausencia del campo no genera error.</summary>
    public bool Required { get; set; } = true;
}
