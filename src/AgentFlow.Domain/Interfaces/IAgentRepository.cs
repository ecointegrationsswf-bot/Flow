using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Repositorio para acceder a agentes y tenants.
/// Necesario para que el handler de mensajes pueda resolver
/// el agente asignado y la API key del tenant.
/// </summary>
public interface IAgentRepository
{
    Task<AgentDefinition?> GetByIdAsync(Guid agentId, CancellationToken ct = default);
    Task<AgentDefinition?> GetFirstActiveByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<CampaignTemplate?> GetCampaignTemplateByIdAsync(Guid templateId, CancellationToken ct = default);
    Task<PromptTemplate?> GetPromptTemplateByIdAsync(Guid promptTemplateId, CancellationToken ct = default);

    /// <summary>
    /// Resuelve el agente activo asociado a una línea WhatsApp específica.
    /// Usado en modo no-Brain: la línea que recibió el mensaje determina qué
    /// agente responde. En modo Brain este método no se invoca (el Cerebro
    /// decide el agente independiente de la línea).
    /// </summary>
    Task<AgentDefinition?> GetActiveByWhatsAppLineAsync(
        Guid tenantId, Guid whatsAppLineId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve el CampaignTemplate primario activo del agente dado, o null
    /// si no hay ninguno. Solo UNO puede ser primario por agente (filtered
    /// unique index). Usado para mensajes orgánicos en modo no-Brain.
    /// </summary>
    Task<CampaignTemplate?> GetPrimaryTemplateForAgentAsync(
        Guid tenantId, Guid agentDefinitionId, CancellationToken ct = default);
}
