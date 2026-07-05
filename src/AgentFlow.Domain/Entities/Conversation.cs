using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Conversación completa entre el agente y un cliente.
/// Una conversación puede cambiar de agente activo (handoff) durante su ciclo de vida.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    // Contacto
    public string ClientPhone { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public string? PolicyNumber { get; set; }
    public ChannelType Channel { get; set; }

    // Agente activo
    public Guid? ActiveAgentId { get; set; }
    public AgentDefinition? ActiveAgent { get; set; }
    public Guid? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }

    // Estado
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;
    public bool IsHumanHandled { get; set; }
    public string? HandledByUserId { get; set; }

    // Resultado de gestión
    public GestionResult GestionResult { get; set; } = GestionResult.Pending;
    public string? ClosingNote { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    // ── Fase 3 — Etiquetado IA ────────────────────────────────────────────
    /// <summary>Etiqueta asignada por el clasificador IA. NULL = aún sin etiquetar.</summary>
    public Guid? LabelId { get; set; }
    public ConversationLabel? Label { get; set; }
    /// <summary>UTC. Cuándo el clasificador asignó la etiqueta.</summary>
    public DateTime? LabeledAt { get; set; }

    /// <summary>
    /// Resultado JSON crudo extraído por el labeling worker basado en
    /// Tenant.LabelingResultSchemaPrompt. Los webhooks lo consultan mapeando
    /// sus campos vía sourceType=labelingResult (ej: result.comentario).
    /// NULL = el tenant no tiene schema configurado o el LLM no devolvió JSON.
    /// </summary>
    public string? LabelingResultJson { get; set; }
    // El envío del webhook de resultado al cliente NO se persiste aquí: es una
    // ActionDefinition programada que se audita en ScheduledWebhookJobExecutions.

    /// <summary>
    /// UTC. Última vez que TransferChatService notificó al ejecutivo (WhatsApp + email)
    /// por esta conversación. Si != NULL, futuras escalaciones NO renotifican
    /// (cooldown de 1-por-conversación). El ejecutivo gestiona desde el portal.
    /// </summary>
    public DateTime? LastTransferChatSentAt { get; set; }

    /// <summary>
    /// Memoria DURABLE de la conversación: bolsa JSON con los resultados de las
    /// acciones ejecutadas, acumulados por slug — forma { "SLUG_ACCION": {responseJson} }.
    /// Se inyecta al agente en CADA turno como "datos_consultados" (mismo canal que
    /// CampaignContact.ContactDataJson), para que pueda responder seguimientos sobre
    /// datos ya consultados (pólizas, saldos, etc.) sin atarse a una ventana de tiempo
    /// ni al flujo que los originó. Genérica por diseño: no es 2FA-específica.
    /// NULL = ninguna acción con response útil se ha ejecutado en esta conversación.
    /// </summary>
    public string? ConversationDataJson { get; set; }

    /// <summary>
    /// Escalamiento robusto — Fase D. Contador de ejecuciones EXITOSAS por slug en esta
    /// conversación — forma { "SLUG_ACCION": n }. Lo usa el GATE anti-loop determinístico para
    /// limitar reintentos (ej: reenvío de código 2FA: INSURED_INITIATE topado en 3) y escalar a
    /// humano al alcanzar el tope. A diferencia de ConversationDataJson, esto NO se inyecta al
    /// prompt (es estado de control, no datos del cliente). NULL = ninguna acción topada ejecutada.
    /// </summary>
    public string? ActionCallCountsJson { get; set; }

    /// <summary>
    /// Motor de flujos — Fase 2. Estado de autenticación PROPIO (no del broker). UTC hasta
    /// cuándo el cliente está autenticado en esta conversación. Se setea cuando una acción
    /// con `AuthPolicy` devuelve un resultado que autentica (ej: status ∈ {VALIDO, AUTO_VALIDADO}).
    /// El gate determinístico (`requiresAuth`) bloquea acciones confidenciales si esto es
    /// null o ya venció. NULL = nunca autenticado / vencido.
    /// </summary>
    public DateTime? AuthenticatedUntil { get; set; }

    /// <summary>Identidad del asegurado autenticado (ej: idEntidad del broker). Para auditoría/contexto.</summary>
    public string? AuthenticatedIdentityId { get; set; }

    /// <summary>
    /// Motor de flujos — Fase 3 (ejecución). Flujo (TenantFlow) que enmarca esta conversación.
    /// Se resuelve por turno desde el maestro primario (CampaignTemplate.ActiveFlowId). Se cachea
    /// aquí para trazabilidad. NULL = la conversación NO tiene flujo activo → el motor no hace nada
    /// y el comportamiento es idéntico al histórico (campañas / inbound libre).
    /// </summary>
    public Guid? ActiveFlowId { get; set; }

    /// <summary>
    /// Motor de flujos — Fase 3. Estado DURABLE de avance del flujo en esta conversación.
    /// JSON con la forma { currentNodeId, slots:{...}, pendingRequest:{actionSlug,params,returnNodeId}, visited:[] }.
    /// `slots` son datos de CONTROL del flujo (cédula, idCodigo) — los datos de NEGOCIO viven en
    /// ConversationDataJson. `pendingRequest` es la solicitud confidencial interceptada por el gate,
    /// a reanudar tras autenticar. NULL = el flujo aún no arrancó / no hay flujo activo.
    /// </summary>
    public string? FlowStateJson { get; set; }

    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<GestionEvent> GestionEvents { get; set; } = [];
}
