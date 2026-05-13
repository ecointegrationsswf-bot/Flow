namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Interpreta la respuesta del webhook según el OutputSchema declarado por el tenant.
/// Ejecuta cada outputAction (send_to_agent, inject_context, log_only,
/// send_whatsapp_media, trigger_escalation) y devuelve el ActionResult final.
/// </summary>
public interface IOutputInterpreter
{
    Task<ActionResult> InterpretAsync(
        string responseBody,
        OutputSchema schema,
        OutputContext context,
        CancellationToken ct = default);
}
