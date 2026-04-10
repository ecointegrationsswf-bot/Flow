using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;

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
    AgentFlow.Domain.Webhooks.IActionExecutorService actionExecutor
) : IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>
{
    public async Task<ProcessIncomingMessageResult> Handle(
        ProcessIncomingMessageCommand cmd, CancellationToken ct)
    {
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

                try
                {
                    agentResponse = await agentRunner.RunAsync(new AgentRunRequest(
                        Agent: agent, Conversation: conversation,
                        IncomingMessage: cmd.Message, RecentHistory: recentHistorySnapshot,
                        ClientContext: clientContext, TenantLlmApiKey: tenantApiKey,
                        MediaUrl: (cmd.MediaType == "image" || cmd.MediaType == "document") ? cmd.MediaUrl : null,
                        MediaType: cmd.MediaType,
                        AttentionDays: attentionDays,
                        AttentionStartTime: attentionStart, AttentionEndTime: attentionEnd
                    ), ct);
                    replyText = agentResponse.ReplyText;

                    // ── Webhook Contract System ────────────────────────────────
                    // Si el agente emitió un tag [ACTION:xxx], intentamos ejecutar
                    // la acción via ActionExecutorService. Si el tenant tiene
                    // WebhookContractEnabled=false o no hay config, es NoOp silencioso.
                    var actionSlug = ExtractActionTag(replyText);
                    if (!string.IsNullOrEmpty(actionSlug))
                    {
                        // Limpiar el tag del texto visible al cliente
                        replyText = RemoveActionTag(replyText);

                        try
                        {
                            var actionResult = await actionExecutor.ExecuteAsync(
                                actionSlug: actionSlug,
                                tenantId: cmd.TenantId,
                                campaignTemplateId: brainCampaignTemplateId
                                    ?? (campaignTemplate?.Id),
                                contactPhone: cmd.FromPhone,
                                conversationId: conversation.Id,
                                collectedParams: AgentFlow.Domain.Webhooks.CollectedParams.Empty(),
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
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ActionExecutor] Error ejecutando {actionSlug}: {ex.Message}");
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
                EscalatedAt: null
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
    /// Elimina el tag [ACTION:xxx] del texto para que el cliente no lo vea.
    /// </summary>
    private static string RemoveActionTag(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return ActionTagRegex.Replace(text, "").Trim();
    }
}
