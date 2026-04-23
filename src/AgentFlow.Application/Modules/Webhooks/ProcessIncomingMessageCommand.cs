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
    string? MediaType = null    // "image" | "document" | "audio"
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
    AgentFlow.Domain.Webhooks.IActionPromptBuilder actionPromptBuilder,
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

        // Flujo original completo — isBrainControlled=false para mantener escalado normal
        return await ExecuteStandardFlow(cmd, dispatch, tenant!, isBrainControlled: false, ct);
    }

    // ═══════════════════════════════════════════════════════
    // BRAIN GATE — flujo alternativo cuando BrainEnabled=true
    // ═══════════════════════════════════════════════════════

    private async Task<ProcessIncomingMessageResult> HandleWithBrain(
        ProcessIncomingMessageCommand cmd, Tenant tenant, CancellationToken ct)
    {
        // ── OPTIMIZACIÓN: Skip del clasificador si hay sesión activa reciente ──
        // Si el cliente está mid-conversación con un agente (< 30 min de inactividad),
        // reutilizamos el mismo agentSlug sin llamar al LLM clasificador.
        // Ahorra ~12 segundos por mensaje en el 80% de los casos.
        BrainDecision? decision = null;
        try
        {
            var session = await sessions.GetAsync(cmd.TenantId, cmd.FromPhone, ct);
            if (session is not null
                && !string.IsNullOrEmpty(session.ActiveAgentSlug)
                && session.BrainState == BrainSessionState.Active_AI
                && DateTime.UtcNow - session.LastActivityAt < TimeSpan.FromMinutes(30))
            {
                // Reutilizar decisión previa — sin clasificar
                decision = new BrainDecision(
                    AgentSlug: session.ActiveAgentSlug,
                    Intent: session.AgentType ?? "continuation",
                    SessionState: BrainSessionState.Active_AI,
                    ValidationPending: false,
                    MessageToClient: null);
            }
        }
        catch { /* Redis no disponible — continuar con clasificación normal */ }

        // ── 1. Si no hubo shortcut, clasificar normalmente ──
        if (decision is null)
        {
            decision = await brainService.RouteAsync(new BrainRequest(
                cmd.TenantId, cmd.FromPhone, cmd.Message, cmd.Channel.ToString(), null), ct);
        }

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

        // Enviar por WhatsApp
        try
        {
            var provider = await channelFactory.GetProviderAsync(cmd.TenantId, ct);
            if (provider is not null)
            {
                var sendResult = await provider.SendMessageAsync(
                    new SendMessageRequest(cmd.FromPhone, decision.MessageToClient!), ct);
                outbound.ExternalMessageId = sendResult.ExternalMessageId;
                outbound.Status = sendResult.Success ? MessageStatus.Sent : MessageStatus.Failed;
                await conversations.SaveChangesAsync(ct);
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
            conversation = (await conversations.GetByIdAsync(dispatch.ExistingConversationId.Value, ct))!;
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

            // Resolver templateId: del Cerebro directo, o desde conversation.CampaignId
            Guid? templateIdToLoad = brainCampaignTemplateId;
            if (templateIdToLoad is null && conversation.CampaignId.HasValue)
            {
                var campaign = await conversations.GetCampaignAsync(conversation.CampaignId.Value, ct);
                templateIdToLoad = campaign?.CampaignTemplateId;
            }

            CampaignTemplate? campaignTemplate = templateIdToLoad.HasValue
                ? await agents.GetCampaignTemplateByIdAsync(templateIdToLoad.Value, ct)
                : null;

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

                // ── CEREBRO: sobreescribir SystemPrompt del agente con el del PromptTemplate ──
                if (isBrainControlled && campaignTemplate is not null)
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

                try
                {
                    // Action Trigger Protocol — Capa 2: carga el catálogo (bloque + diccionario
                    // slug→TriggerConfig) una sola vez. El bloque se inyecta al system prompt; el
                    // diccionario se usa después para validar el [ACTION] que emita el agente.
                    var actionCatalog = await actionPromptBuilder.GetCatalogAsync(
                        campaignTemplateId: brainCampaignTemplateId ?? campaignTemplate?.Id,
                        tenantId: cmd.TenantId,
                        ct: ct);

                    // PDFs de referencia del maestro de campaña — se inyectan al prompt
                    // para que el agente los use como contexto al responder.
                    var referenceDocs = campaignTemplate?.Documents is { Count: > 0 }
                        ? campaignTemplate.Documents
                            .Select(d => new AgentFlow.Domain.Interfaces.ReferenceDocument(d.FileName, d.BlobUrl))
                            .ToList()
                        : null;

                    Console.WriteLine($"[WH-REFDOCS] template={campaignTemplate?.Id} templateName={campaignTemplate?.Name} docs={campaignTemplate?.Documents?.Count ?? -1} refDocsPassed={referenceDocs?.Count ?? 0}");

                    agentResponse = await agentRunner.RunAsync(new AgentRunRequest(
                        Agent: agent, Conversation: conversation,
                        IncomingMessage: cmd.Message, RecentHistory: recentHistorySnapshot,
                        ClientContext: clientContext, TenantLlmApiKey: tenantApiKey,
                        MediaUrl: (cmd.MediaType == "image" || cmd.MediaType == "document") ? cmd.MediaUrl : null,
                        MediaType: cmd.MediaType,
                        AttentionDays: attentionDays,
                        AttentionStartTime: attentionStart, AttentionEndTime: attentionEnd,
                        ActionsBlock: actionCatalog.Block,
                        LastActionResult: lastActionForPrompt,
                        ReferenceDocuments: referenceDocs
                    ), ct);
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
                            try
                            {
                                var collectedParams = new AgentFlow.Domain.Webhooks.CollectedParams
                                {
                                    Values = parsedParams
                                };

                                var actionResult = await actionExecutor.ExecuteAsync(
                                    actionSlug: actionSlug,
                                    tenantId: cmd.TenantId,
                                    campaignTemplateId: brainCampaignTemplateId
                                        ?? (campaignTemplate?.Id),
                                    contactPhone: cmd.FromPhone,
                                    conversationId: conversation.Id,
                                    collectedParams: collectedParams,
                                    agentSlug: agent.Name,
                                    ct: ct);

                                // Si la acción devolvió datos para el agente, apendicarlos al replyText
                                if (actionResult.Success && !string.IsNullOrEmpty(actionResult.DataForAgent))
                                {
                                    replyText = string.IsNullOrEmpty(replyText)
                                        ? actionResult.DataForAgent
                                        : $"{replyText}\n\n{actionResult.DataForAgent}";
                                }
                                // Si falló con mensaje controlado, usarlo como respuesta
                                else if (!actionResult.Success && !string.IsNullOrEmpty(actionResult.ErrorMessage))
                                {
                                    replyText = string.IsNullOrEmpty(replyText)
                                        ? actionResult.ErrorMessage
                                        : $"{replyText}\n\n{actionResult.ErrorMessage}";
                                }

                                // Si la acción pidió escalar, forzar ShouldEscalate en la respuesta
                                if (actionResult.ShouldEscalate)
                                {
                                    agentResponse = agentResponse with { ShouldEscalate = true };
                                }

                                // Action Trigger Protocol Fase 4: si la acción se ejecutó con éxito,
                                // persistir el resultado en Redis para que esté disponible en el
                                // siguiente turno (Flujo D) y auditar la ejecución con GestionEvent.
                                if (actionResult.Success)
                                {
                                    lastActionForPersist = new AgentFlow.Domain.Webhooks.LastActionResult(
                                        Slug: actionSlug,
                                        DataForAgent: actionResult.DataForAgent,
                                        ExecutedAt: DateTime.UtcNow);

                                    try
                                    {
                                        var notes = actionResult.DataForAgent ?? $"Acción {actionSlug} ejecutada.";
                                        if (notes.Length > 400) notes = notes[..400];

                                        await conversations.AddGestionEventAsync(new AgentFlow.Domain.Entities.GestionEvent
                                        {
                                            Id = Guid.NewGuid(),
                                            ConversationId = conversation.Id,
                                            Result = AgentFlow.Domain.Enums.GestionResult.Pending,
                                            Origin = $"agent:action:{actionSlug.ToLowerInvariant()}",
                                            Notes = notes,
                                            OccurredAt = DateTime.UtcNow
                                        }, ct);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning(ex, "[ATP] No se pudo registrar GestionEvent para {ActionSlug}", actionSlug);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "[ATP] Error ejecutando acción {ActionSlug}", actionSlug);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    replyText = "Gracias por su mensaje. En este momento no puedo procesar su solicitud. Un ejecutivo se comunicará con usted a la brevedad.";
                    agentResponse = new AgentResponse(replyText, dispatch.Intent, 0, false, false, 0);
                    Console.WriteLine($"[AgentRunner] Error: {ex.Message}");
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

                // Enviar por WhatsApp
                try
                {
                    var provider = await channelFactory.GetProviderAsync(cmd.TenantId, ct);
                    if (provider is not null)
                    {
                        var sendResult = await provider.SendMessageAsync(
                            new SendMessageRequest(cmd.FromPhone, replyText), ct);
                        outbound.ExternalMessageId = sendResult.ExternalMessageId;
                        outbound.Status = sendResult.Success ? MessageStatus.Sent : MessageStatus.Failed;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WhatsApp] Error enviando respuesta: {ex.Message}");
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
}
