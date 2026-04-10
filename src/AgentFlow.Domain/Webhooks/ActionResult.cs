namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Resultado de ejecutar una acción con webhook.
/// </summary>
public class ActionResult
{
    public bool Success { get; init; }

    /// <summary>Texto consolidado para el agente IA (resultado de outputActions send_to_agent).</summary>
    public string? DataForAgent { get; init; }

    /// <summary>True si algún campo con outputAction=trigger_escalation evaluó como falsy.</summary>
    public bool ShouldEscalate { get; init; }

    /// <summary>Mensaje controlado al cliente cuando hay un error recuperable.</summary>
    public string? ErrorMessage { get; init; }

    public int HttpStatusCode { get; init; }

    /// <summary>Resultado neutro — no se ejecutó nada (sin schema configurado o acción deshabilitada).</summary>
    public static ActionResult NoOp() => new() { Success = true };

    /// <summary>Resultado exitoso con datos para el agente.</summary>
    public static ActionResult Ok(string? dataForAgent = null, bool shouldEscalate = false, int httpStatus = 200) =>
        new() { Success = true, DataForAgent = dataForAgent, ShouldEscalate = shouldEscalate, HttpStatusCode = httpStatus };

    /// <summary>Resultado fallido con mensaje al cliente.</summary>
    public static ActionResult Fail(string errorMessage, int httpStatus = 0) =>
        new() { Success = false, ErrorMessage = errorMessage, HttpStatusCode = httpStatus };
}
