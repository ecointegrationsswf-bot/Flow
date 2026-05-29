using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Application.Modules.Webhooks;

/// <summary>
/// Comando principal: procesa un mensaje entrante de cualquier canal.
/// Orquesta TODO el flujo: Dispatcher → Memoria → Agente IA → Respuesta → Notificación.
/// </summary>
public record ProcessIncomingMessageCommand(
    Guid TenantId,
    string FromPhone,
    string Message,
    ChannelType Channel,
    string? ClientName,
    string? ExternalMessageId,
    string? MediaUrl = null,    // URL pública del archivo (Azure Blob o UltraMsg)
    string? MediaType = null,   // "image" | "document" | "audio"
    Guid? WhatsAppLineId = null // Línea WhatsApp que recibió el mensaje (resuelta por
                                // WebhookController desde instanceId). En no-Brain
                                // esta línea es la que determina qué agente responde.
) : IRequest<ProcessIncomingMessageResult>;

public record ProcessIncomingMessageResult(
    Guid ConversationId,
    string ReplyText,
    bool WasEscalated,
    bool WasClosed,
    string AgentType
);

/// <summary>
/// Handler que orquesta el flujo completo de un mensaje entrante:
///
/// 1. DISPATCHER — Identifica conversación existente o crea una nueva
/// 2. PERSISTIR MENSAJE — Guarda el mensaje del cliente en BD
/// 3. MEMORIA — Carga los últimos 10 mensajes de la conversación (historial para el LLM)
/// 4. RESOLVER AGENTE — Busca el AgentDefinition con su system prompt y config
/// 5. EJECUTAR LLM — Llama a Claude/OpenAI con: system prompt + historial + mensaje nuevo
/// 6. PERSISTIR RESPUESTA — Guarda la respuesta del agente en BD
/// 7. ENVIAR POR WHATSAPP — Envía la respuesta al cliente via UltraMsg
/// 8. ACTUALIZAR SESIÓN — Redis guarda el estado para continuidad
/// 9. NOTIFICAR MONITOR — SignalR envía evento al frontend en tiempo real
/// </summary>
public class ProcessIncomingMessageHandler(
    IContextDispatcher dispatcher,
    IAgentRunner agentRunner,
    IConversationRepository conversations,
    IAgentRepository agents,
    IChannelProviderFactory channelFactory,
    ISessionStore sessions,
    IConversationNotifier notifier,
    ITransferChatService transferChat,
    ISendEmailResumeService emailResume,
    IBrainService brainService,
    IAgentRegistry agentRegistry,
    ICampaignRepository campaignRepo,
    AgentFlow.Domain.Webhooks.IActionExecutorService actionExecutor,
    AgentFlow.Domain.Webhooks.IActionChainResolver actionChainResolver,
    AgentFlow.Domain.Webhooks.IActionPromptBuilder actionPromptBuilder,
    IDocumentReferencePromptBuilder documentReferencePromptBuilder,
    IWebhookEventDispatcher eventDispatcher,
    ISystemAuditLogger systemAudit,
    Microsoft.Extensions.Logging.ILogger<ProcessIncomingMessageHandler> logger
) : IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>
{
    public async Task<ProcessIncomingMessageResult> Handle(
        ProcessIncomingMessageCommand cmd, CancellationToken ct)
    {
        Console.WriteLine($"[WH-INBOUND] tenant={cmd.TenantId} from={cmd.FromPhone} msg=\"{cmd.Message?.Substring(0, Math.Min(80, cmd.Message?.Length ?? 0))}\"");
        // ── BRAIN GATE: si el tenant tiene el Cerebro activado, usar BrainService ──
        var tenant = await agents.GetTenantByIdAsync(cmd.TenantId, ct);
        if (tenant?.BrainEnabled == true)
        {
            return await HandleWithBrain(cmd, tenant, ct);
        }

        // ── 1. DISPATCHER: ¿qué agente responde? (flujo original) ────────
        var dispatch = await dispatcher.DispatchAsync(new(
            cmd.TenantId, cmd.FromPhone, cmd.Message, cmd.Channel.ToString()), ct);

        // ── 1.b ROUTING POR LÍNEA WHATSAPP (no-Brain) ───────────────────
        // Si el mensaje entró por una línea conocida (cmd.WhatsAppLineId) y
        // el tenant NO tiene Cerebro, la línea es la fuente de verdad: el
        // agente que responde es el que tiene WhatsAppLineId = ese GUID.
        //
        // Esto OVERRIDE la decisión del dispatcher (sesión Redis / campaña /
        // LLM clasificador). Razón: en el plan básico cada agente tiene su
        // propia línea y cualquier mensaje que entre por X debe ser atendido
        // por el agente dueño de X — sin ambigüedad.
        //
        // Si no encontramos agente para esa línea, dejamos que el flujo
        // estándar haga su mejor esfuerzo y eventualmente caiga al canned +
        // escalación humana ya implementado.
        if (cmd.WhatsAppLineId.HasValue)
        {
            var agentByLine = await agents.GetActiveByWhatsAppLineAsync(
                cmd.TenantId, cmd.WhatsAppLineId.Value, ct);
            if (agentByLine is not null && agentByLine.Id != dispatch.SelectedAgentId)
            {
                logger.LogInformation(
                    "Routing por línea (no-Brain): tenant={Tenant} line={Line} agent={Agent} (override sobre dispatcher={Prev})",
                    cmd.TenantId, cmd.WhatsAppLineId, agentByLine.Id, dispatch.SelectedAgentId);
                dispatch = dispatch with { SelectedAgentId = agentByLine.Id };
            }
            else if (agentByLine is null)
            {
                logger.LogWarning(
                    "Línea {Line} no tiene agente activo asignado en tenant {Tenant} — se usa fallback del dispatcher.",
                    cmd.WhatsAppLineId, cmd.TenantId);
            }
        }

        // Flujo original completo — isBrainControlled=false para mantener escalado normal
        return await ExecuteStandardFlow(cmd, dispatch, tenant!, isBrainControlled: false, ct);
    }

    // ═══════════════════════════════════════════════════════
    // BRAIN GATE — flujo alternativo cuando BrainEnabled=true
    // ═══════════════════════════════════════════════════════

    private async Task<ProcessIncomingMessageResult> HandleWithBrain(
        ProcessIncomingMessageCommand cmd, Tenant tenant, CancellationToken ct)
    {
        // ── CLASIFICACIÓN POR TURNO ──
        // El Cerebro se ejecuta en CADA mensaje entrante. Antes había un shortcut
        // que reutilizaba la decisión de Redis si la sesión tenía < 30 min de
        // inactividad, pero eso impedía que el Cerebro detectara cambios de tema
        // (ej: conversación de cobros que deriva a reclamos). La corrección:
        // reclasificar siempre — Anthropic prompt caching amortiza el costo del LLM.
        var decision = await brainService.RouteAsync(new BrainRequest(
            cmd.TenantId, cmd.FromPhone, cmd.Message, cmd.Channel.ToString(), null), ct);

        // ── 2. Estado terminal → silencio ──
        if (decision.SessionState == BrainSessionState.HumanClosed)
        {
            return new ProcessIncomingMessageResult(Guid.Empty, "", false, false, "closed");
        }

        // ── 3. El Cerebro quiere hablarle directamente al cliente ──
        if (!string.IsNullOrEmpty(decision.MessageToClient))
        {
            return await SendBrainDirectMessage(cmd, decision, ct);
        }

        // ── 4. Routing normal — el Cerebro eligió un agente ──
        var registryEntry = await agentRegistry.GetBySlugAsync(cmd.TenantId, decision.AgentSlug, ct);
        var agentId = registryEntry?.AgentDefinitionId;
        var brainCampaignTemplateId = registryEntry?.CampaignTemplateId;

        var existingConv = await conversations.GetLatestByPhoneAsync(cmd.TenantId, cmd.FromPhone, ct);

        // ── OVERRIDE: respetar la campaña activa de la conversación ──
        // El AgentRegistry mapea slug→template global del tenant (ej: "cobros" → "Copia
        // de Cobros Pasesa"). Pero si la conversación viene de una campaña con OTRO
        // template (ej: "Cobros Prueba II"), el mensaje INICIAL salió con ese template
        // y la RESPUESTA debe seguir usando el mismo — si no, el cliente ve dos
        // identidades distintas (ej: "Sofía/SOMOS Seguros" al inicio y "PASESA" al
        // responder). Aquí priorizamos la campaña activa sobre el registry global.
        if (existingConv?.CampaignId is not null
            && existingConv.Status != ConversationStatus.Closed)
        {
            var campaign = await conversations.GetCampaignAsync(existingConv.CampaignId.Value, ct);
            var isCampaignLive = campaign is not null
                && campaign.IsActive
                && campaign.Status != Domain.Enums.CampaignStatus.Completed
                && campaign.Status != Domain.Enums.CampaignStatus.Cancelled
                && campaign.Status != Domain.Enums.CampaignStatus.Failed;
            if (isCampaignLive && campaign!.CampaignTemplateId.HasValue)
            {
                logger.LogInformation(
                    "Brain: conversación {ConvId} pertenece a campaña activa {CampaignId} — respetando su template {TemplateId} en lugar del slug '{Slug}' del AgentRegistry.",
                    existingConv.Id, campaign.Id, campaign.CampaignTemplateId, decision.AgentSlug);
                brainCampaignTemplateId = campaign.CampaignTemplateId;
                agentId = campaign.AgentDefinitionId;
            }
        }

        var dispatch = new DispatchResult(
            ExistingConversationId: existingConv?.Id,
            SelectedAgentId: agentId,
            Intent: decision.Intent,
            IsExistingSession: existingConv is not null,
            // Si la conversación viene de una campaña, marcarlo para que se inyecten
            // los datos del ContactDataJson al clientContext
            IsCampaignContact: existingConv?.CampaignId is not null,
            CampaignId: existingConv?.CampaignId);

        return await ExecuteStandardFlow(cmd, dispatch, tenant, isBrainControlled: true, ct,
            brainCampaignTemplateId: brainCampaignTemplateId,
            preloadedConversation: existingConv,
            brainAgentSlug: decision.AgentSlug);
    }

    /// <summary>
    /// El Cerebro envía un mensaje directo al cliente (validación, bienvenida, pregunta).
    /// No pasa por el agente IA.
    /// </summary>
    private async Task<ProcessIncomingMessageResult> SendBrainDirectMessage(
        ProcessIncomingMessageCommand cmd, BrainDecision decision, CancellationToken ct)
    {
        // Obtener o crear conversación
        Conversation? conversation = null;
        try
        {
            var existingSession = await sessions.GetAsync(cmd.TenantId, cmd.FromPhone, ct);
            if (existingSession is not null)
                conversation = await conversations.GetByIdAsync(existingSession.ConversationId, ct);
        }
        catch { }

        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                TenantId = cmd.TenantId,
                ClientPhone = cmd.FromPhone,
                ClientName = cmd.ClientName,
                Channel = cmd.Channel,
                Status = ConversationStatus.Active,
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
            conversation = await conversations.CreateAsync(conversation, ct);
        }

        // Guardar mensaje entrante
        var inbound = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered,
            Content = cmd.Message,
            ExternalMessageId = cmd.ExternalMessageId,
            IsFromAgent = false,
            SentAt = DateTime.UtcNow
        };
        await conversations.AddMessageAsync(inbound, ct);

        // Marcar grupo de morosidad como respondido (sólo si autoCrearCampanas creó la campaña)
        await campaignRepo.TryMarkContactGroupRepliedAsync(cmd.TenantId, cmd.FromPhone, inbound.SentAt, ct);

        // Guardar respuesta del Cerebro
        var outbound = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent,
            Content = decision.MessageToClient!,
            IsFromAgent = true,
            AgentName = "Cerebro",
            DetectedIntent = decision.Intent,
            SentAt = DateTime.UtcNow
        };
        await conversations.AddMessageAsync(outbound, ct);
        conversation.LastActivityAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(ct);

        // Enviar por WhatsApp — segmentado en burbujas si el texto trae '~' (igual
        // criterio que el flujo del agente). Sin '~' es un único mensaje, idéntico
        // al comportamiento previo.
        try
        {
            var provider = await channelFactory.GetProviderAsync(cmd.TenantId, ct);
            if (provider is not null)
            {
                var bubbles = SplitIntoBubbles(decision.MessageToClient!);
                string? lastExternalId = null;
                var lastSuccess = false;
                var sentAny = false;
                for (var bi = 0; bi < bubbles.Count; bi++)
                {
                    if (bi > 0) await Task.Delay(BubbleDelayMs, ct);
                    var r = await provider.SendMessageAsync(
                        new SendMessageRequest(cmd.FromPhone, bubbles[bi]), ct);
                    lastExternalId = r.ExternalMessageId;
                    lastSuccess = r.Success;
                    sentAny = true;
                }
                if (sentAny)
                {
                    outbound.ExternalMessageId = lastExternalId;
                    outbound.Status = lastSuccess ? MessageStatus.Sent : MessageStatus.Failed;
                    await conversations.SaveChangesAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Brain] Error enviando mensaje directo: {ex.Message}");
        }

        // Notificar monitor
        try
        {
            await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
            {
                Type = "outbound",
                ConversationId = conversation.Id,
                Body = decision.MessageToClient,
                AgentName = "Cerebro",
                Intent = decision.Intent,
                Timestamp = DateTime.UtcNow
            });
        }
        catch { }

        return new ProcessIncomingMessageResult(
            conversation.Id, decision.MessageToClient!, false, false, decision.Intent);
    }

    /// <summary>
    /// Flujo estándar (pasos 2-13) reutilizado por BrainGate y flujo original.
    /// Cuando isBrainControlled=true, no evalúa ShouldEscalate (el Cerebro ya lo manejó).
    /// </summary>
    private async Task<ProcessIncomingMessageResult> ExecuteStandardFlow(
        ProcessIncomingMessageCommand cmd, DispatchResult dispatch, Tenant tenant,
        bool isBrainControlled, CancellationToken ct,
        Guid? brainCampaignTemplateId = null,
        Conversation? preloadedConversation = null,
        string? brainAgentSlug = null)
    {
        // ── 2. OBTENER O CREAR CONVERSACIÓN ──────────────
        Conversation conversation;
        if (preloadedConversation is not null)
        {
            // Conversación ya cargada por HandleWithBrain — evitar segunda query
            conversation = preloadedConversation;
            if (conversation.Status == ConversationStatus.Closed ||
                conversation.Status == ConversationStatus.Unresponsive ||
                conversation.Status == ConversationStatus.WaitingClient)
            {
                conversation.Status = ConversationStatus.Active;
                conversation.IsHumanHandled = false;
                conversation.LastActivityAt = DateTime.UtcNow;
                if (cmd.ClientName is not null)
                    conversation.ClientName = cmd.ClientName;
            }
        }
        else if (dispatch.IsExistingSession && dispatch.ExistingConversationId.HasValue)
        {
            // Defense: la sesión Redis puede apuntar a una Conversation que ya
            // no existe en BD (mantenimiento, restore parcial, DELETE manual).
            // Sin esta validación, GetByIdAsync retorna null y el ! del operador
            // null-forgiveness deja pasar el null, reventando con NRE en la
            // siguiente línea. Si está null, caemos al else (crear nueva Conv).
            var loaded = await conversations.GetByIdAsync(dispatch.ExistingConversationId.Value, ct);
            if (loaded is null)
            {
                logger.LogWarning(
                    "Sesión Redis apunta a Conversation {ConvId} que no existe en BD — creo una nueva. tenant={Tenant} phone={Phone}",
                    dispatch.ExistingConversationId.Value, cmd.TenantId, cmd.FromPhone);
                conversation = new Conversation
                {
                    Id = Guid.NewGuid(),
                    TenantId = cmd.TenantId,
                    ClientPhone = cmd.FromPhone,
                    ClientName = cmd.ClientName,
                    Channel = cmd.Channel,
                    ActiveAgentId = dispatch.SelectedAgentId,
                    CampaignId = dispatch.CampaignId,
                    Status = ConversationStatus.Active,
                    StartedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                };
                await conversations.CreateAsync(conversation, ct);
            }
            else
            {
                conversation = loaded;
            }
            if (conversation.Status == ConversationStatus.Closed ||
                conversation.Status == ConversationStatus.Unresponsive ||
                conversation.Status == ConversationStatus.WaitingClient)
            {
                conversation.Status = ConversationStatus.Active;
                conversation.IsHumanHandled = false;
                conversation.LastActivityAt = DateTime.UtcNow;
                if (cmd.ClientName is not null)
                    conversation.ClientName = cmd.ClientName;
            }
        }
        else
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                TenantId = cmd.TenantId,
                ClientPhone = cmd.FromPhone,
                ClientName = cmd.ClientName,
                Channel = cmd.Channel,
                ActiveAgentId = dispatch.SelectedAgentId,
                CampaignId = dispatch.CampaignId,
                Status = ConversationStatus.Active,
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
            conversation = await conversations.CreateAsync(conversation, ct);
        }

        // ── 3. PERSISTIR MENSAJE ENTRANTE ────────────────
        // Ampliado a 20 mensajes para conversaciones largas — los datos del cliente
        // ya no dependen del historial (van en clientContext) pero el contexto
        // conversacional sigue siendo importante.
        var recentHistorySnapshot = conversation.Messages?
            .OrderByDescending(m => m.SentAt).Take(20).OrderBy(m => m.SentAt).ToList() ?? [];

        var inboundContent = cmd.Message;
        if (!string.IsNullOrEmpty(cmd.MediaUrl))
            inboundContent = $"{cmd.Message}\n[media:{cmd.MediaUrl}]";

        var inbound = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered,
            Content = inboundContent,
            ExternalMessageId = cmd.ExternalMessageId,
            IsFromAgent = false,
            SentAt = DateTime.UtcNow
        };
        conversation.LastActivityAt = DateTime.UtcNow;
        await conversations.AddMessageAsync(inbound, ct);
        await conversations.SaveChangesAsync(ct);

        // Marcar grupo de morosidad como respondido (sólo si autoCrearCampanas creó la campaña)
        await campaignRepo.TryMarkContactGroupRepliedAsync(cmd.TenantId, cmd.FromPhone, inbound.SentAt, ct);

        // Notificar monitor
        try
        {
            await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
            {
                Type = "inbound", ConversationId = conversation.Id,
                From = cmd.FromPhone, Body = cmd.Message,
                ClientName = cmd.ClientName, Timestamp = DateTime.UtcNow
            });
        }
        catch { }

        // ── 4. EJECUTAR AGENTE IA ────────────────────────
        AgentResponse? agentResponse = null;
        string replyText = "";

        // Action Trigger Protocol Fase 4: slot a nivel de método para el nuevo
        // LastActionResult producido por este turno (si se ejecutó una acción).
        // Se lee al final, en el SetAsync de la sesión. null si no hubo acción.
        AgentFlow.Domain.Webhooks.LastActionResult? lastActionForPersist = null;

        if (!conversation.IsHumanHandled)
        {
            var agentId = dispatch.SelectedAgentId ?? conversation.ActiveAgentId;

            AgentDefinition? agent = null;
            if (agentId.HasValue)
                agent = await agents.GetByIdAsync(agentId.Value, ct);
            agent ??= await agents.GetFirstActiveByTenantAsync(cmd.TenantId, ct);

            // Resolver templateId: PRIORIDAD al template de la campaña activa porque
            // ese contexto es el más específico del cliente (system prompt, PDFs de
            // referencia, horarios, etc.).
            //
            // PRIORIDAD DEL MAESTRO A CARGAR:
            // 1) Si el Cerebro reenrutó a un agente del AgentRegistry
            //    (brainCampaignTemplateId tiene valor), usar el maestro de ESE agente.
            //    Así el prompt, PDFs y configuración corresponden al agente que
            //    realmente va a responder — no al maestro de la campaña original.
            //    Ej: conversación nace de campaña "Cobros" pero el cliente pregunta
            //    por reclamos → Cerebro activa agente "reclamos" → cargamos el
            //    maestro "Reclamos" con sus PDFs médicos.
            // 2) Si no hubo reenrutado del Cerebro pero la conversación tiene
            //    CampaignId (outbound histórico), usar el maestro de esa campaña.
            // 3) Inbound libre sin Cerebro ni campaña → null.
            Guid? templateIdToLoad = null;
            if (brainCampaignTemplateId.HasValue)
            {
                templateIdToLoad = brainCampaignTemplateId;
            }
            else if (conversation.CampaignId.HasValue)
            {
                var campaign = await conversations.GetCampaignAsync(conversation.CampaignId.Value, ct);
                templateIdToLoad = campaign?.CampaignTemplateId;
            }

            CampaignTemplate? campaignTemplate = templateIdToLoad.HasValue
                ? await agents.GetCampaignTemplateByIdAsync(templateIdToLoad.Value, ct)
                : null;

            // ── Caso 3 — Mensaje orgánico en no-Brain ────────────────────
            // Si no hubo reenrutado del Cerebro NI campaña asociada, el
            // mensaje es orgánico (cliente escribe sin contexto de campaña).
            // En el plan básico el agente ya quedó resuelto por la línea
            // (paso 1.b). Cargamos su CampaignTemplate primario para usar
            // su SystemPrompt + documentos. Sin esto el agente se quedaría
            // sin prompt y caería al canned + escalación.
            if (campaignTemplate is null
                && !isBrainControlled
                && agent is not null)
            {
                campaignTemplate = await agents.GetPrimaryTemplateForAgentAsync(
                    cmd.TenantId, agent.Id, ct);
                if (campaignTemplate is not null)
                {
                    templateIdToLoad = campaignTemplate.Id;
                    logger.LogInformation(
                        "Maestro primario resuelto para agente {Agent}: {Template} ('{Name}')",
                        agent.Id, campaignTemplate.Id, campaignTemplate.Name);
                }
            }

            if (agent is not null)
            {
                if (conversation.ActiveAgentId != agent.Id)
                    conversation.ActiveAgentId = agent.Id;

                var tenantApiKey = tenant.LlmApiKey;

                // Horario de atención desde el template ya cargado
                List<int>? attentionDays = null;
                string? attentionStart = null, attentionEnd = null;
                if (campaignTemplate is not null)
                {
                    attentionDays = campaignTemplate.AttentionDays;
                    attentionStart = campaignTemplate.AttentionStartTime;
                    attentionEnd = campaignTemplate.AttentionEndTime;
                }

                // ── Source of truth del prompt: SIEMPRE el CampaignTemplate asociado ──
                // El AgentDefinition.SystemPrompt nunca se llena por UI (campo legacy).
                // El único prompt válido es el del Maestro de Campaña al que el Cerebro
                // (o la campaña activa) vinculó al agente.
                if (campaignTemplate is not null)
                {
                    if (!string.IsNullOrEmpty(campaignTemplate.SystemPrompt))
                    {
                        agent.SystemPrompt = campaignTemplate.SystemPrompt;
                    }
                    else if (campaignTemplate.PromptTemplateIds.Count > 0)
                    {
                        var promptTemplate = await agents.GetPromptTemplateByIdAsync(
                            campaignTemplate.PromptTemplateIds[0], ct);
                        if (promptTemplate is not null && !string.IsNullOrEmpty(promptTemplate.SystemPrompt))
                        {
                            agent.SystemPrompt = promptTemplate.SystemPrompt;
                        }
                    }
                }

                var clientContext = new Dictionary<string, string>
                {
                    ["nombre"] = cmd.ClientName ?? conversation.ClientName ?? "Cliente",
                    ["telefono"] = cmd.FromPhone
                };

                // ── Inyectar datos del Excel de la campaña (ContactDataJson) ──
                // Si la conversación proviene de una campaña, cargar los datos del contacto
                // del Excel original para que el agente tenga TODA la información disponible
                // (póliza, aseguradora, monto, producto, etc.) en cada respuesta,
                // sin depender del historial de mensajes.
                if (conversation.CampaignId.HasValue)
                {
                    var campaignContact = await campaignRepo.GetContactByPhoneAsync(
                        conversation.CampaignId.Value, cmd.FromPhone, ct);

                    if (campaignContact is not null)
                    {
                        // Campos fuertemente tipados del contacto
                        if (!string.IsNullOrEmpty(campaignContact.PolicyNumber))
                            clientContext["poliza"] = campaignContact.PolicyNumber;
                        if (!string.IsNullOrEmpty(campaignContact.InsuranceCompany))
                            clientContext["aseguradora"] = campaignContact.InsuranceCompany;
                        if (campaignContact.PendingAmount.HasValue)
                            clientContext["monto_pendiente"] = campaignContact.PendingAmount.Value.ToString("F2");

                        // Aplanar ContactDataJson (todas las columnas del Excel)
                        if (!string.IsNullOrEmpty(campaignContact.ContactDataJson))
                        {
                            try
                            {
                                var records = System.Text.Json.JsonSerializer
                                    .Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(
                                        campaignContact.ContactDataJson);

                                if (records is not null && records.Count > 0)
                                {
                                    // Si es 1 solo registro → aplanar cada campo al contexto
                                    if (records.Count == 1)
                                    {
                                        foreach (var (key, val) in records[0])
                                        {
                                            var strVal = val.ValueKind == System.Text.Json.JsonValueKind.String
                                                ? val.GetString() ?? ""
                                                : val.ToString();
                                            if (!string.IsNullOrEmpty(strVal))
                                                clientContext[key] = strVal;
                                        }
                                    }
                                    // Múltiples registros (cliente con varias pólizas) → inyectar JSON completo
                                    else
                                    {
                                        clientContext["total_registros"] = records.Count.ToString();
                                        clientContext["datos_completos"] = campaignContact.ContactDataJson;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ClientContext] Error parseando ContactDataJson: {ex.Message}");
                            }
                        }
                    }
                }

                // Action Trigger Protocol Fase 4: cargar sesión previa para saber si hay
                // un LastActionResult pendiente del turno anterior que el agente deba ver.
                // Si existe y es fresco, se inyecta al prompt en ESTE turno y luego se limpia
                // (consume-on-read) — si una nueva acción fire ahora, la reemplaza.
                AgentFlow.Domain.Webhooks.LastActionResult? lastActionForPrompt = null;
                try
                {
                    var prevSession = await sessions.GetAsync(cmd.TenantId, cmd.FromPhone, ct);
                    if (prevSession?.LastActionResult is { } prevLast && prevLast.IsFresh())
                        lastActionForPrompt = prevLast;
                }
                catch { /* si Redis falla, seguimos sin inyectar — retrocompat */ }

                // ── GUARD: si el agente quedó sin prompt resoluble (sin Cerebro ni
                // CampaignTemplate vinculado), NO procesamos con IA. Respondemos con
                // mensaje canned + escalamos a humano para que el equipo atienda.
                // AgentDefinition.SystemPrompt es legacy y nunca debe usarse como fallback.
                if (string.IsNullOrWhiteSpace(agent.SystemPrompt))
                {
                    replyText = "Hola, recibimos tu mensaje 😊 En breve un asesor del equipo te atenderá.";
                    agentResponse = new AgentResponse(
                        ReplyText: replyText,
                        DetectedIntent: "humano",
                        ConfidenceScore: 1.0,
                        ShouldEscalate: true,
                        ShouldClose: false,
                        TokensUsed: 0);
                    conversation.Status = ConversationStatus.EscalatedToHuman;
                    try { await transferChat.ExecuteIfApplicableAsync(conversation, ct); }
                    catch (Exception exTc) { Console.WriteLine($"[Canned/TransferChat] {exTc.Message}"); }
                    try { await notifier.NotifyEscalationAsync(cmd.TenantId.ToString(), conversation.Id); }
                    catch { }
                    logger.LogWarning(
                        "Conv {ConvId} sin CampaignTemplate resoluble (sin Cerebro y sin CampaignId) — respuesta canned + escalada a humano. Tenant={TenantId}, Agent={AgentName}",
                        conversation.Id, cmd.TenantId, agent.Name);
                }
                else try
                {
                    // Action Trigger Protocol — Capa 2: carga el catálogo (bloque + diccionario
                    // slug→TriggerConfig) una sola vez. El bloque se inyecta al system prompt; el
                    // diccionario se usa después para validar el [ACTION] que emita el agente.
                    var actionCatalog = await actionPromptBuilder.GetCatalogAsync(
                        campaignTemplateId: brainCampaignTemplateId ?? campaignTemplate?.Id,
                        tenantId: cmd.TenantId,
                        ct: ct);

                    // PDFs de referencia — modo TENANT-WIDE.
                    // Antes: solo se cargaban los PDFs del maestro activo, lo que dejaba al
                    // agente "Cobros" sin acceso a los PDFs subidos al maestro "Reclamos"
                    // aunque ambos pertenecieran al mismo corredor. Esto producía
                    // alucinaciones (direcciones inventadas, declinaciones falsas) cuando el
                    // Cerebro enrutaba a un agente cuyo maestro no tenía documentos.
                    //
                    // Ahora: el builder carga TODOS los PDFs del tenant, priorizando los del
                    // maestro activo. Tope defensivo de 5 PDFs por turno (TenantDocsCap).
                    // Feature flag Tenant.ReferenceDocumentsEnabled controla la inyección.
                    var refDocsEnabled = tenant?.ReferenceDocumentsEnabled == true;
                    var useRag = tenant?.UseRagRetrieval == true;
                    List<AgentFlow.Domain.Interfaces.ReferenceDocument>? referenceDocs = null;
                    string? referenceDocsBlock = null;

                    if (refDocsEnabled)
                    {
                        var prioritizeId = brainCampaignTemplateId ?? campaignTemplate?.Id;

                        if (useRag)
                        {
                            // ── MODO RAG ─────────────────────────────────────
                            // Recuperamos solo los chunks más relevantes a la pregunta
                            // del cliente. No adjuntamos PDFs como base64 — los
                            // fragmentos van directo en el system prompt como texto.
                            // Costo ~95% menor que adjuntar PDFs enteros + escala a
                            // muchos más documentos por maestro sin chocar context.
                            referenceDocsBlock = await documentReferencePromptBuilder.BuildRagContextForQueryAsync(
                                prioritizeId, cmd.TenantId, cmd.Message ?? string.Empty, ct);

                            logger.LogInformation(
                                "ReferenceDocs RAG: tenant={TenantId} prioritize={PrioritizeId} blockChars={BlockChars}",
                                cmd.TenantId, prioritizeId, referenceDocsBlock?.Length ?? 0);
                        }
                        else
                        {
                            // ── MODO LEGACY (PDFs enteros) ───────────────────
                            // Comportamiento previo: lista de PDFs + reglas en el
                            // system prompt, los PDFs base64 adjuntos al request.
                            var tenantDocs = await documentReferencePromptBuilder.GetTenantDocumentsAsync(
                                prioritizeId, cmd.TenantId, ct);

                            if (tenantDocs.Count > 0)
                            {
                                referenceDocs = tenantDocs.ToList();
                                referenceDocsBlock = await documentReferencePromptBuilder.BuildTextBlockAsync(
                                    prioritizeId, cmd.TenantId, ct);

                                logger.LogInformation(
                                    "ReferenceDocs (tenant-wide) inyectados: tenant={TenantId} prioritize={PrioritizeId} docs={DocCount} blockChars={BlockChars}",
                                    cmd.TenantId, prioritizeId, referenceDocs.Count, referenceDocsBlock?.Length ?? 0);
                            }
                        }
                    }

                    Console.WriteLine($"[WH-REFDOCS] tenant={cmd.TenantId} prioritize={brainCampaignTemplateId ?? campaignTemplate?.Id} refDocsPassed={referenceDocs?.Count ?? 0} blockLen={referenceDocsBlock?.Length ?? 0}");

                    // Reintento defensivo. Anthropic API ocasionalmente devuelve 5xx
                    // transitorios o se cuelga por algunos segundos cuando se le
                    // mandan PDFs grandes (3 docs × ~varios MB). Sin esto, una sola
                    // falla resulta en "no puedo procesar su solicitud" al cliente,
                    // aunque la siguiente llamada hubiera funcionado. Reintentamos
                    // UNA vez con un pequeño delay; si la segunda también falla, deja
                    // que la excepción la atrape el catch externo (fallback al cliente).
                    var runRequest = new AgentRunRequest(
                        Agent: agent, Conversation: conversation,
                        IncomingMessage: cmd.Message, RecentHistory: recentHistorySnapshot,
                        ClientContext: clientContext, TenantLlmApiKey: tenantApiKey,
                        MediaUrl: (cmd.MediaType == "image" || cmd.MediaType == "document") ? cmd.MediaUrl : null,
                        MediaType: cmd.MediaType,
                        AttentionDays: attentionDays,
                        AttentionStartTime: attentionStart, AttentionEndTime: attentionEnd,
                        ActionsBlock: actionCatalog.Block,
                        LastActionResult: lastActionForPrompt,
                        ReferenceDocuments: referenceDocs,
                        ReferenceDocumentsBlock: referenceDocsBlock
                    );

                    // Reintentos con backoff escalonado. Anthropic devuelve dos tipos
                    // de fallos transitorios:
                    //   • RateLimitsExceeded — el budget de tokens/req por minuto se
                    //     agotó. Requiere ESPERAR varios segundos para que se libere.
                    //   • 5xx / timeouts puntuales — se resuelven en 1-2 segundos.
                    // Por eso usamos delays distintos según el tipo de excepción y
                    // hasta 3 intentos en total (1 original + 2 reintentos).
                    agentResponse = null!;
                    Exception? lastEx = null;
                    int[] delaysMs = [0, 0, 0]; // se decide en runtime según el tipo
                    for (var attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            agentResponse = await agentRunner.RunAsync(runRequest, ct);
                            lastEx = null;
                            break;
                        }
                        catch (Exception exRetry) when (!ct.IsCancellationRequested)
                        {
                            lastEx = exRetry;
                            if (attempt == 3) break; // ya consumimos los 3 — tira fallback

                            // Rate limit → delay grande (5s, 12s). Otros transitorios → 1.5s, 3s.
                            var isRateLimit = exRetry.GetType().Name.Contains("RateLimit", StringComparison.OrdinalIgnoreCase)
                                              || exRetry.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                                              || exRetry.Message.Contains("429");
                            var delayMs = isRateLimit
                                ? (attempt == 1 ? 5000 : 12000)
                                : (attempt == 1 ? 1500 : 3000);

                            logger.LogWarning(exRetry,
                                "[AgentRunner] Intento {N} falló ({ExType}{RL}) conv {ConvId} tenant {TenantId} — reintentando en {Delay}ms",
                                attempt, exRetry.GetType().Name,
                                isRateLimit ? " [rate-limit]" : "",
                                conversation.Id, cmd.TenantId, delayMs);
                            await Task.Delay(delayMs, ct);
                        }
                    }
                    if (lastEx is not null) throw lastEx;

                    replyText = agentResponse.ReplyText;

                    // ── Webhook Contract System ────────────────────────────────
                    // Si el agente emitió un tag [ACTION:xxx], intentamos ejecutar
                    // la acción via ActionExecutorService. Si el tenant tiene
                    // WebhookContractEnabled=false o no hay config, es NoOp silencioso.
                    var actionSlug = ExtractActionTag(replyText);
                    if (!string.IsNullOrEmpty(actionSlug))
                    {
                        // Action Trigger Protocol — Fase 3: extraer params [PARAM:k=v] y limpiar
                        // todos los tags ([ACTION] y [PARAM]) del texto visible al cliente.
                        var parsedParams = ExtractActionParams(replyText);
                        replyText = RemoveAllActionTags(replyText);

                        // ── Validación 1 — soft whitelist ──
                        // Si el catálogo está activo (el tenant usa Action Trigger Protocol)
                        // y el slug emitido por el agente NO está en el catálogo, bloqueamos
                        // la ejecución. Si el catálogo está vacío (tenant legacy), dejamos pasar
                        // y el ActionExecutorService hará su propia validación de template.
                        var shouldExecute = true;
                        if (actionCatalog.IsActive && !actionCatalog.Contains(actionSlug))
                        {
                            logger.LogWarning(
                                "[ATP] Slug {ActionSlug} fuera del catálogo — ejecución bloqueada. Permitidos: [{AllowedSlugs}]",
                                actionSlug, string.Join(",", actionCatalog.BySlug.Keys));
                            shouldExecute = false;
                        }

                        // ── Validación 2 — requiresConfirmation ──
                        // Si la acción tiene campos que el agente debía confirmar y alguno
                        // falta en los [PARAM:...], no ejecutamos — el agente debió pedir
                        // la confirmación antes. El texto del agente (ya limpio) se envía
                        // igual, asumiendo que contenía la pregunta de aclaración.
                        if (shouldExecute && actionCatalog.Get(actionSlug) is { RequiresConfirmation: { Count: > 0 } required })
                        {
                            var missing = required
                                .Where(f => !parsedParams.TryGetValue(f, out var v) || string.IsNullOrEmpty(v))
                                .ToList();
                            if (missing.Count > 0)
                            {
                                logger.LogWarning(
                                    "[ATP] Slug {ActionSlug} disparado sin confirmar campos: [{MissingFields}] — ejecución bloqueada",
                                    actionSlug, string.Join(",", missing));
                                shouldExecute = false;
                            }
                        }

                        if (shouldExecute)
                        {
                            // ── Loop de auto-encadenamiento (ChainRules, Paso 5 del wizard) ──
                            // Ejecuta la acción A, evalúa sus chainRules contra el response,
                            // si matchea encadena la acción B (mismos tenant params, sin LLM en medio).
                            // Guards: max depth=3, sin repetir slugs (anti-ciclo).
                            //
                            // El LLM solo redacta UNA vez al cliente, con el LastActionResult
                            // del ÚLTIMO eslabón del chain. Las acciones intermedias quedan
                            // en WebhookDispatchLogs + GestionEvent para auditoría.
                            const int MaxChainDepth = 3;
                            var executedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var currentSlug = actionSlug;
                            var currentParams = parsedParams;
                            // chainOverrides contiene los campos del JSON response del eslabón anterior,
                            // aplanados como "lastActionResult.<key>". El PayloadBuilder los leerá cuando
                            // un InputField declare sourceType=lastActionResult. Para el PRIMER eslabón
                            // sembramos con el RawResponseJson del LastActionResult del TURNO previo
                            // (persistido en Redis): así una acción puede leer datos del turno anterior
                            // sin que el LLM tenga que pasárselos vía [PARAM:...].
                            IReadOnlyDictionary<string, string?>? chainOverrides =
                                FlattenJsonForChain(lastActionForPrompt?.RawResponseJson);
                            // Snapshot del RawResponseJson del PRIMER eslabón — lo que vamos a persistir
                            // en Redis al final del turno. Las acciones encadenadas suelen ser "puente"
                            // sin response útil; el dato relevante para el siguiente turno está en la
                            // acción que el LLM invocó (la origen).
                            string? firstChainRawResponse = null;
                            // Acumulador del successMessage del último ChainTarget que matcheó (con
                            // placeholders ya interpolados contra el response del eslabón ORIGEN).
                            // Se apendica al replyText al cerrar el loop.
                            string? chainSuccessMessage = null;
                            // Si alguna regla del chain tiene RegenerateReply=true, recordamos el flag
                            // para hacer una SEGUNDA invocación al LLM tras cerrar el loop, descartando
                            // el "validando..." preliminar.
                            bool chainRegenerateReply = false;
                            var depth = 0;

                            while (!string.IsNullOrEmpty(currentSlug) && depth < MaxChainDepth)
                            {
                                if (!executedSlugs.Add(currentSlug))
                                {
                                    logger.LogWarning("[ATP-Chain] Ciclo detectado: {Slug} ya se ejecutó en este turno. Cortando chain.", currentSlug);
                                    break;
                                }

                                AgentFlow.Domain.Webhooks.ActionResult actionResult;
                                try
                                {
                                    var collectedParams = new AgentFlow.Domain.Webhooks.CollectedParams
                                    {
                                        Values = currentParams
                                    };

                                    actionResult = await actionExecutor.ExecuteAsync(
                                        actionSlug: currentSlug,
                                        tenantId: cmd.TenantId,
                                        campaignTemplateId: brainCampaignTemplateId
                                            ?? (campaignTemplate?.Id),
                                        contactPhone: cmd.FromPhone,
                                        conversationId: conversation.Id,
                                        collectedParams: collectedParams,
                                        agentSlug: agent.Name,
                                        systemContextOverrides: chainOverrides,
                                        ct: ct);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "[ATP] Error ejecutando acción {ActionSlug} (depth={Depth})", currentSlug, depth);
                                    break;
                                }

                                // Mensajes para el cliente: solo apendear DataForAgent/ErrorMessage del
                                // ÚLTIMO eslabón (las acciones intermedias son transparentes).
                                if (actionResult.Success && !string.IsNullOrEmpty(actionResult.DataForAgent))
                                {
                                    replyText = string.IsNullOrEmpty(replyText)
                                        ? actionResult.DataForAgent
                                        : $"{replyText}\n\n{actionResult.DataForAgent}";
                                }
                                else if (!actionResult.Success && !string.IsNullOrEmpty(actionResult.ErrorMessage))
                                {
                                    replyText = string.IsNullOrEmpty(replyText)
                                        ? actionResult.ErrorMessage
                                        : $"{replyText}\n\n{actionResult.ErrorMessage}";
                                }

                                if (actionResult.ShouldEscalate)
                                {
                                    agentResponse = agentResponse with { ShouldEscalate = true };
                                }

                                if (actionResult.Success)
                                {
                                    // Snapshot del response del PRIMER eslabón (depth=0). Lo usaremos al
                                    // final del turno como base del LastActionResult persistido en Redis,
                                    // y para interpolar el successMessage de las ChainRules.
                                    if (depth == 0)
                                        firstChainRawResponse = actionResult.RawResponseJson;

                                    // LastActionResult persistido refleja el PRIMER eslabón (la acción
                                    // que el LLM invocó). Razón: las acciones encadenadas suelen ser
                                    // "puente" con response vacío (ej: SEND_2FA_CODE_EMAIL); los datos
                                    // útiles para el próximo turno viven en el response de la origen.
                                    var persistSlug = depth == 0 ? currentSlug : (lastActionForPersist?.Slug ?? currentSlug);
                                    var persistData = depth == 0 ? actionResult.DataForAgent : (lastActionForPersist?.DataForAgent);
                                    lastActionForPersist = new AgentFlow.Domain.Webhooks.LastActionResult(
                                        Slug: persistSlug,
                                        DataForAgent: persistData,
                                        ExecutedAt: DateTime.UtcNow,
                                        RawResponseJson: firstChainRawResponse);

                                    try
                                    {
                                        var notes = actionResult.DataForAgent ?? $"Acción {currentSlug} ejecutada.";
                                        if (notes.Length > 400) notes = notes[..400];

                                        await conversations.AddGestionEventAsync(new AgentFlow.Domain.Entities.GestionEvent
                                        {
                                            Id = Guid.NewGuid(),
                                            ConversationId = conversation.Id,
                                            Result = AgentFlow.Domain.Enums.GestionResult.Pending,
                                            Origin = $"agent:action:{currentSlug.ToLowerInvariant()}",
                                            Notes = notes,
                                            OccurredAt = DateTime.UtcNow
                                        }, ct);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning(ex, "[ATP] No se pudo registrar GestionEvent para {ActionSlug}", currentSlug);
                                    }
                                }
                                else
                                {
                                    // Si falló, NO encadenamos (el chain solo continúa tras success).
                                    break;
                                }

                                // ── Resolver siguiente acción del chain ──
                                // Lee chainRules del contract de currentSlug y evalúa contra el JSON crudo
                                // del response. Si matchea, devuelve la decisión: slug + successMessage opcional.
                                AgentFlow.Domain.Webhooks.ChainDecision? decision = null;
                                try
                                {
                                    decision = await actionChainResolver.GetNextActionAsync(
                                        executedSlug: currentSlug,
                                        tenantId: cmd.TenantId,
                                        rawResponseJson: actionResult.RawResponseJson,
                                        ct: ct);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "[ATP-Chain] Error resolviendo siguiente acción tras {Slug}", currentSlug);
                                }

                                if (decision is null)
                                    break;

                                // Interpolar successMessage contra el JSON del eslabón ORIGEN (el que tenía
                                // la regla). Lo guardamos para apendar al cerrar el loop — así si el chain
                                // tiene varios eslabones, gana el del último matching rule.
                                if (!string.IsNullOrWhiteSpace(decision.SuccessMessageTemplate))
                                {
                                    chainSuccessMessage = InterpolateTemplate(
                                        decision.SuccessMessageTemplate,
                                        actionResult.RawResponseJson);
                                }

                                // Si la regla pide regenerate, lo marcamos. El handler hará la 2da invocación
                                // al LLM al cerrar el loop. Esto puede coexistir con SuccessMessage (ej:
                                // "Identidad confirmada" como mensaje fijo + respuesta natural del LLM).
                                if (decision.RegenerateReply)
                                    chainRegenerateReply = true;

                                // Si no hay próxima acción (rama documental con regenerate=true), cortamos.
                                if (string.IsNullOrEmpty(decision.NextSlug))
                                    break;

                                // Aplanar el JSON response del eslabón actual a un Dictionary<string,string?>
                                // con keys "lastActionResult.<campo>" para que el PayloadBuilder del siguiente
                                // eslabón lo pueda leer (sourceType=lastActionResult).
                                chainOverrides = FlattenJsonForChain(actionResult.RawResponseJson);

                                // La acción encadenada toma sus inputs del lastActionResult del eslabón
                                // anterior (sourceType=lastActionResult en su InputSchema). No heredamos
                                // [PARAM:...] del LLM — solo aplican al primer eslabón.
                                currentSlug = decision.NextSlug;
                                currentParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                depth++;
                            }

                            if (depth >= MaxChainDepth)
                            {
                                logger.LogWarning("[ATP-Chain] Límite de profundidad {Max} alcanzado. Slugs ejecutados: [{Chain}]",
                                    MaxChainDepth, string.Join(" → ", executedSlugs));
                            }

                            // ── POST-CHAIN — regenerar replyText con el LLM ──────────────────
                            // Si alguna ChainRule tenía RegenerateReply=true, hacemos UNA segunda
                            // invocación al LLM con el LastActionResult ya populado. El runner
                            // añade una directiva POST_CHAIN al system prompt que le dice al LLM
                            // que la acción ya corrió y debe redactar la respuesta final SIN
                            // emitir más [ACTION:...]. Reemplaza el "validando..." preliminar
                            // por una respuesta natural que vuelve a la pregunta original.
                            //
                            // Costo: 1 round-trip extra al LLM por turno con regenerate. Solo
                            // se invoca cuando se configuró explícitamente en una ChainRule.
                            if (chainRegenerateReply && lastActionForPersist is not null)
                            {
                                try
                                {
                                    var regenRequest = runRequest with
                                    {
                                        LastActionResult = lastActionForPersist,
                                        PostChainRegeneration = true
                                    };
                                    var regenerated = await agentRunner.RunAsync(regenRequest, ct);
                                    var newText = RemoveAllActionTags(regenerated.ReplyText ?? string.Empty);
                                    if (!string.IsNullOrWhiteSpace(newText))
                                    {
                                        replyText = newText;
                                        logger.LogInformation("[ATP-Chain] Reply regenerado tras chain ({Len} chars)", newText.Length);
                                    }
                                    else
                                    {
                                        logger.LogWarning("[ATP-Chain] Regeneración devolvió vacío — conservo el reply preliminar.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "[ATP-Chain] Regeneración del reply falló — conservo el reply preliminar.");
                                }
                            }

                            // Apendar el successMessage del chain (si hubo) al replyText. Así el
                            // cliente recibe en el MISMO mensaje del agente la confirmación de que
                            // se ejecutó la acción encadenada. Ej: "Te envié un código a j****@...".
                            if (!string.IsNullOrWhiteSpace(chainSuccessMessage))
                            {
                                replyText = string.IsNullOrEmpty(replyText)
                                    ? chainSuccessMessage
                                    : $"{replyText}\n\n{chainSuccessMessage}";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    replyText = "Gracias por su mensaje. En este momento no puedo procesar su solicitud. Un ejecutivo se comunicará con usted a la brevedad.";
                    agentResponse = new AgentResponse(replyText, dispatch.Intent, 0, false, false, 0);
                    logger.LogError(ex,
                        "[AgentRunner] Falla tras reintento para conv {ConvId} tenant {TenantId} agent {Agent} — cliente recibió fallback. Mensaje original: '{Msg}'",
                        conversation.Id, cmd.TenantId, agent.Name,
                        cmd.Message?.Length > 120 ? cmd.Message[..120] : cmd.Message);

                    // Persistencia en doble lugar (auditabilidad robusta):
                    //   1) GestionEvent ligado a la conversación — visible en el detalle
                    //      del Monitor para ver el caso específico.
                    //   2) SystemAuditLog — tabla cross-tenant para queries agregadas:
                    //      "todos los errores AGENT_RUN de PASESA en las últimas 24h".
                    try
                    {
                        var stack = ex.ToString();
                        var notes = $"AGENT EXCEPTION — Tipo: {ex.GetType().FullName}\n" +
                                    $"Mensaje cliente: {(cmd.Message?.Length > 200 ? cmd.Message[..200] : cmd.Message)}\n" +
                                    $"Stack:\n{(stack.Length > 1500 ? stack[..1500] : stack)}";
                        await conversations.AddGestionEventAsync(new AgentFlow.Domain.Entities.GestionEvent
                        {
                            Id = Guid.NewGuid(),
                            ConversationId = conversation.Id,
                            Result = AgentFlow.Domain.Enums.GestionResult.Pending,
                            Origin = "system:agent-exception",
                            Notes = notes,
                            OccurredAt = DateTime.UtcNow
                        }, ct);
                    }
                    catch (Exception logEx)
                    {
                        logger.LogWarning(logEx, "[AgentRunner] No se pudo persistir GestionEvent del fallo.");
                    }

                    await systemAudit.LogErrorAsync(
                        category: "AGENT_RUN",
                        message: $"Agente falló para conversación {conversation.Id}",
                        ex: ex,
                        tenantId: cmd.TenantId,
                        relatedEntityType: "Conversation",
                        relatedEntityId: conversation.Id,
                        contextJson: System.Text.Json.JsonSerializer.Serialize(new
                        {
                            agent = agent.Name,
                            clientPhone = cmd.FromPhone,
                            messagePreview = cmd.Message?.Length > 200 ? cmd.Message[..200] : cmd.Message
                        }),
                        ct: ct);
                }

                // ── Salvavidas del historial Anthropic ──
                // Si el LLM emitió solo [ACTION:...] sin texto y la(s) acción(es) del chain
                // no produjeron DataForAgent visible, replyText queda en string.Empty tras
                // limpiar los tags. Persistir Content="" rompe el siguiente turno (Anthropic
                // rechaza "messages: text content blocks must be non-empty"). Marcamos
                // este turno como "técnico": persistimos en BD un placeholder para mantener
                // el historial válido, pero NO se notifica al monitor ni se envía por WhatsApp.
                var hasVisibleReply = !string.IsNullOrWhiteSpace(replyText);
                if (!hasVisibleReply)
                {
                    replyText = "[turno técnico — acción ejecutada sin texto al cliente]";
                }

                // Persistir respuesta
                var outbound = new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Direction = MessageDirection.Outbound,
                    Status = MessageStatus.Sent,
                    Content = replyText,
                    IsFromAgent = true,
                    AgentName = agent.AvatarName ?? agent.Name,
                    DetectedIntent = agentResponse.DetectedIntent,
                    ConfidenceScore = agentResponse.ConfidenceScore,
                    TokensUsed = agentResponse.TokensUsed,
                    SentAt = DateTime.UtcNow
                };
                await conversations.AddMessageAsync(outbound, ct);
                conversation.LastActivityAt = DateTime.UtcNow;
                await conversations.SaveChangesAsync(ct);

                // Solo notificar al monitor / enviar por WhatsApp si HUBO texto visible.
                // Cuando hasVisibleReply=false el outbound persistido es el placeholder
                // "[turno técnico ...]" — útil para mantener el historial Anthropic válido
                // pero NO debe llegarle al cliente ni al ejecutivo en el monitor.
                if (hasVisibleReply)
                {
                    try
                    {
                        await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
                        {
                            Type = "outbound", ConversationId = conversation.Id,
                            Body = replyText, AgentName = agent.AvatarName ?? agent.Name,
                            Intent = agentResponse.DetectedIntent, Timestamp = DateTime.UtcNow
                        });
                    }
                    catch { }

                    // Enviar por WhatsApp — segmentado en burbujas si el texto trae '~'
                    // (lo usa el prompt de AFTA). Sin '~' es un único mensaje, igual que antes.
                    try
                    {
                        var provider = await channelFactory.GetProviderAsync(cmd.TenantId, ct);
                        if (provider is not null)
                        {
                            var bubbles = SplitIntoBubbles(replyText);
                            string? lastExternalId = null;
                            var lastSuccess = false;
                            var sentAny = false;
                            for (var bi = 0; bi < bubbles.Count; bi++)
                            {
                                if (bi > 0) await Task.Delay(BubbleDelayMs, ct);
                                var r = await provider.SendMessageAsync(
                                    new SendMessageRequest(cmd.FromPhone, bubbles[bi]), ct);
                                lastExternalId = r.ExternalMessageId;
                                lastSuccess = r.Success;
                                sentAny = true;
                            }
                            if (sentAny)
                            {
                                outbound.ExternalMessageId = lastExternalId;
                                outbound.Status = lastSuccess ? MessageStatus.Sent : MessageStatus.Failed;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WhatsApp] Error enviando respuesta: {ex.Message}");
                    }
                }
                else
                {
                    logger.LogInformation("[Chain] Turno técnico para conv {ConvId}: acciones ejecutadas sin texto visible al cliente.", conversation.Id);
                }

                // Escalado — solo si NO es controlado por el Cerebro (evitar doble escalado)
                if (!isBrainControlled && agentResponse.ShouldEscalate)
                {
                    conversation.Status = ConversationStatus.EscalatedToHuman;
                    try { await transferChat.ExecuteIfApplicableAsync(conversation, ct); }
                    catch (Exception ex) { Console.WriteLine($"[TransferChat] Error: {ex.Message}"); }
                    try { await notifier.NotifyEscalationAsync(cmd.TenantId.ToString(), conversation.Id); }
                    catch { }
                }

                if (agentResponse.ShouldClose)
                {
                    conversation.Status = ConversationStatus.Closed;
                    conversation.ClosedAt = DateTime.UtcNow;
                    try { await emailResume.ExecuteIfApplicableAsync(conversation, ct); }
                    catch (Exception ex) { Console.WriteLine($"[SendEmailResume] Error: {ex.Message}"); }

                    // Hook ScheduledWebhookWorker — programa jobs EventBased/DelayFromEvent
                    // suscritos a "ConversationClosed" (etiquetado IA, webhook resultado, etc.).
                    try { await eventDispatcher.DispatchAsync("ConversationClosed", conversation.Id.ToString(), cmd.TenantId, ct); }
                    catch (Exception ex) { Console.WriteLine($"[EventDispatch] ConversationClosed: {ex.Message}"); }
                }
            }
        }

        // Actualizar sesión Redis (incluye slug del Cerebro para shortcut en próximos mensajes)
        try
        {
            var brainState = conversation.IsHumanHandled
                ? BrainSessionState.Escalated_Human
                : BrainSessionState.Active_AI;

            await sessions.SetAsync(cmd.TenantId, cmd.FromPhone, new SessionState(
                ConversationId: conversation.Id,
                AgentId: conversation.ActiveAgentId ?? dispatch.SelectedAgentId ?? Guid.Empty,
                AgentType: agentResponse?.DetectedIntent ?? dispatch.Intent,
                CampaignId: dispatch.CampaignId,
                IsHumanHandled: conversation.IsHumanHandled,
                LastActivityAt: DateTime.UtcNow,
                BrainState: brainState,
                Origin: SessionOrigin.Inbound,
                ActiveCampaignId: dispatch.CampaignId,
                ActiveAgentSlug: brainAgentSlug,
                IntentHistory: null,
                ValidationState: null,
                EscalatedAt: null,
                // Action Trigger Protocol Fase 4 — consume-on-read:
                // Si este turno produjo un nuevo LastActionResult, persistirlo.
                // Si no, el valor queda null — el del turno anterior (que ya se
                // inyectó al prompt en este turno) no se propaga a otro más.
                LastActionResult: lastActionForPersist
            ), TimeSpan.FromHours(72), ct);
        }
        catch { }

        conversation.LastActivityAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(ct);

        return new ProcessIncomingMessageResult(
            conversation.Id, replyText,
            agentResponse?.ShouldEscalate ?? false,
            agentResponse?.ShouldClose ?? false,
            agentResponse?.DetectedIntent ?? dispatch.Intent
        );
    }

    // ── Webhook Contract System — helpers de parsing del tag [ACTION:xxx] ──

    private static readonly System.Text.RegularExpressions.Regex ActionTagRegex =
        new(@"\[ACTION:([A-Z_][A-Z0-9_]*)\]", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Action Trigger Protocol — Fase 3: [PARAM:nombre=valor]
    // Permite: letras, dígitos y guiones bajos en el nombre; cualquier cosa que no sea ']' en el valor.
    // El valor "null" (literal) se interpreta como C# null en ExtractActionParams.
    private static readonly System.Text.RegularExpressions.Regex ParamTagRegex =
        new(@"\[PARAM:(\w+)=([^\]]*)\]", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Extrae el slug de una acción del texto del agente.
    /// Formato esperado: [ACTION:SEND_PAYMENT_LINK]
    /// Devuelve null si no hay tag.
    /// </summary>
    private static string? ExtractActionTag(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = ActionTagRegex.Match(text);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Action Trigger Protocol — Fase 3.
    /// Extrae todos los [PARAM:nombre=valor] del texto del agente. El valor "null"
    /// (literal, sin comillas) se interpreta como C# null. Los valores se pasan tal
    /// cual al PayloadBuilder, que aplicará coerción por dataType del InputSchema.
    /// </summary>
    private static Dictionary<string, string?> ExtractActionParams(string? text)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text)) return result;

        foreach (System.Text.RegularExpressions.Match m in ParamTagRegex.Matches(text))
        {
            var name = m.Groups[1].Value;
            var value = m.Groups[2].Value.Trim();
            // Placeholder "<valor confirmado>" que el agente podría dejar si no reemplazó: lo tratamos como vacío.
            if (value.StartsWith('<') && value.EndsWith('>'))
                value = string.Empty;
            result[name] = string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) || value.Length == 0
                ? null
                : value;
        }

        return result;
    }

    /// <summary>
    /// Elimina el tag [ACTION:xxx] del texto para que el cliente no lo vea.
    /// Nota: conservado por retrocompat. Nuevos callers deben usar RemoveAllActionTags.
    /// </summary>
    private static string RemoveActionTag(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return ActionTagRegex.Replace(text, "").Trim();
    }

    /// <summary>
    /// Action Trigger Protocol — Fase 3.
    /// Limpia del texto visible al cliente tanto [ACTION:xxx] como todos los [PARAM:k=v].
    /// </summary>
    private static string RemoveAllActionTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var noAction = ActionTagRegex.Replace(text, "");
        var noParams = ParamTagRegex.Replace(noAction, "");
        return noParams.Trim();
    }

    /// <summary>
    /// Aplana un JSON crudo (response de la acción anterior del chain) a un diccionario
    /// con keys "lastActionResult.&lt;campo&gt;". Solo procesa el primer nivel del objeto raíz
    /// (no anida); valores complejos se serializan a string. Devuelve null si el input
    /// no es un JSON object válido — el orquestador trata null como "sin overrides".
    ///
    /// Estos overrides los aplica el ActionExecutorService al SystemContext, permitiendo
    /// que InputSchema fields con sourceType=lastActionResult.sourceKey resuelvan al valor
    /// real del eslabón anterior (ej: SEND_2FA_CODE_EMAIL leyendo "correoDestino" de la
    /// respuesta de INSURED_INITIATE).
    /// </summary>
    /// <summary>
    /// Reemplaza placeholders `{path}` en un template por valores del JSON crudo.
    /// El path soporta dot-notation simple (case-insensitive). Placeholders sin
    /// match se sustituyen por string.Empty. Si rawJson es null/invalido devuelve
    /// el template tal cual (sin interpolar).
    ///
    /// Ejemplo: "Te envié un código a {correoEnmascarado}" con
    ///          rawJson={"correoEnmascarado":"j****@hotmail.com"}
    ///          → "Te envié un código a j****@hotmail.com"
    /// </summary>
    private static string InterpolateTemplate(string template, string? rawJson)
    {
        if (string.IsNullOrEmpty(template)) return template;
        if (string.IsNullOrWhiteSpace(rawJson)) return template;

        System.Text.Json.JsonDocument doc;
        try { doc = System.Text.Json.JsonDocument.Parse(rawJson); }
        catch { return template; }

        using (doc)
        {
            var root = doc.RootElement;
            return System.Text.RegularExpressions.Regex.Replace(template, @"\{([^{}]+)\}", m =>
            {
                var path = m.Groups[1].Value.Trim();
                var value = ResolveJsonPath(root, path);
                return value ?? string.Empty;
            });
        }
    }

    // ── Segmentación de mensajes en "burbujas" ──────────────────────────────
    // Algunos prompts (ej. AFTA) separan párrafos con '~' para que el cliente
    // reciba mensajes WhatsApp SEPARADOS en vez de un solo recuadro. El envío
    // divide por '~'; si el texto no trae '~' es un único mensaje (comportamiento
    // previo intacto). Es per-tenant "gratis": lo activa el prompt, no un flag.
    private const int BubbleDelayMs = 1000;   // pausa entre burbujas (orden + naturalidad)
    private const int MaxBubbles = 6;         // tope defensivo

    /// <summary>
    /// Parte el texto en burbujas por el separador '~'. Sin '~' devuelve un solo
    /// elemento (= un único mensaje). Recorta, descarta vacías y limita a
    /// MaxBubbles (el excedente se une en la última con saltos de línea).
    /// </summary>
    private static List<string> SplitIntoBubbles(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        if (!text.Contains('~')) return new List<string> { text };
        var parts = text
            .Split('~', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        if (parts.Count == 0) return new List<string> { text };
        if (parts.Count <= MaxBubbles) return parts;
        var capped = parts.Take(MaxBubbles - 1).ToList();
        capped.Add(string.Join("\n\n", parts.Skip(MaxBubbles - 1)));
        return capped;
    }

    /// <summary>Resuelve un path dot-notation sobre un JsonElement (case-insensitive). Mismo helper
    /// que ActionChainResolver — duplicado adrede para no agregar acoplamiento entre Application e
    /// Infrastructure por algo trivial.</summary>
    private static string? ResolveJsonPath(System.Text.Json.JsonElement root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            if (current.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            System.Text.Json.JsonElement next = default;
            bool found = false;
            foreach (var prop in current.EnumerateObject())
            {
                if (prop.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    next = prop.Value;
                    found = true;
                    break;
                }
            }
            if (!found) return null;
            current = next;
        }
        return current.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => current.GetString(),
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Undefined => null,
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            _ => current.ToString()
        };
    }

    private static IReadOnlyDictionary<string, string?>? FlattenJsonForChain(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

            var flat = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => prop.Value.GetString(),
                    System.Text.Json.JsonValueKind.Null => null,
                    System.Text.Json.JsonValueKind.Undefined => null,
                    System.Text.Json.JsonValueKind.True => "true",
                    System.Text.Json.JsonValueKind.False => "false",
                    _ => prop.Value.ToString()
                };
                flat[$"lastActionResult.{prop.Name}"] = value;
            }
            return flat.Count > 0 ? flat : null;
        }
        catch
        {
            return null;
        }
    }
}
