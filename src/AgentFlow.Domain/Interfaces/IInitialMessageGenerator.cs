using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Genera el mensaje inicial de campaña usando el LLM del tenant. Reemplaza
/// el template hardcoded del CampaignDispatcherService por la misma lógica
/// que usa N8nCallbackController (Claude + prompt del CampaignTemplate +
/// contexto desde ContactDataJson).
/// </summary>
public interface IInitialMessageGenerator
{
    /// <summary>
    /// Devuelve el texto del mensaje a enviar. Si la campaña no tiene prompt
    /// configurado, el tenant no tiene <c>LlmApiKey</c>, o el LLM falla,
    /// devuelve null — el caller debe decidir el fallback (ej: template básico).
    /// </summary>
    Task<string?> GenerateAsync(
        Campaign campaign,
        CampaignContact contact,
        CancellationToken ct = default);
}
