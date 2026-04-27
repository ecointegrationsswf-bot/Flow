namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Construye el SystemContext para un request de ejecución de acción.
/// Lee de BD: Contact, Campaign, Tenant, Conversation.
/// </summary>
public interface ISystemContextBuilder
{
    Task<SystemContext> BuildAsync(
        Guid tenantId,
        Guid? campaignId,
        string contactPhone,
        Guid? conversationId,
        string? agentSlug = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fase 3 — Etiquetado IA. Construye el contexto sobre el resultado FINAL de
    /// una conversación cerrada y etiquetada. Extiende BuildAsync con sourceKeys
    /// específicos del resultado:
    ///   conversation.label.name | .slug | .confidence | .reasoning | .extractedDate
    ///   conversation.closedAt | .closeReason | .messageCount
    ///   contact.externalId (del ContactDataJson) | campaign.externalRef
    ///
    /// Usado por el ConversationLabelingJob al disparar el webhook de resultado.
    /// </summary>
    Task<SystemContext> BuildResultContextAsync(
        Guid conversationId, CancellationToken ct = default);
}
