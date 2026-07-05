namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Resultado de ejecutar una acción con webhook.
///
/// Es un record class para permitir `result with { RawResponseJson = ... }`
/// desde el orquestador, sin romper el patrón de fábricas estáticas.
/// </summary>
public record ActionResult
{
    public bool Success { get; init; }

    /// <summary>Texto consolidado para el agente IA (resultado de outputActions send_to_agent).</summary>
    public string? DataForAgent { get; init; }

    /// <summary>True si algún campo con outputAction=trigger_escalation evaluó como falsy.</summary>
    public bool ShouldEscalate { get; init; }

    /// <summary>Mensaje controlado al cliente cuando hay un error recuperable.</summary>
    public string? ErrorMessage { get; init; }

    public int HttpStatusCode { get; init; }

    /// <summary>
    /// JSON crudo de la respuesta HTTP (body sin parsear). Lo usa el motor
    /// de auto-encadenamiento (ChainRule) para evaluar condiciones sobre el
    /// response sin tener que pasar por el OutputSchema parseado.
    /// Null cuando no hay body o cuando el call fue NoOp.
    /// </summary>
    public string? RawResponseJson { get; init; }

    /// <summary>
    /// URLs públicas (Azure Blob) de los documentos/medios que la acción envió al cliente por
    /// WhatsApp (outputAction=send_whatsapp_media). Quedan de RESPALDO y el handler las adjunta al
    /// mensaje del agente como tags [media:URL] para que el documento se vea en el Monitor.
    /// Null/vacío cuando la acción no envió media.
    /// </summary>
    public IReadOnlyList<string>? MediaUrls { get; init; }

    /// <summary>Resultado neutro — no se ejecutó nada (sin schema configurado o acción deshabilitada).</summary>
    public static ActionResult NoOp() => new() { Success = true };

    /// <summary>Resultado exitoso con datos para el agente.</summary>
    public static ActionResult Ok(string? dataForAgent = null, bool shouldEscalate = false, int httpStatus = 200) =>
        new() { Success = true, DataForAgent = dataForAgent, ShouldEscalate = shouldEscalate, HttpStatusCode = httpStatus };

    /// <summary>Resultado fallido con mensaje al cliente.</summary>
    public static ActionResult Fail(string errorMessage, int httpStatus = 0) =>
        new() { Success = false, ErrorMessage = errorMessage, HttpStatusCode = httpStatus };
}
