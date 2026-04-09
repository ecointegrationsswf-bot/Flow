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
    IAgentRegistry agentRegistry
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
        // ── 1. El Cerebro decide ──
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

        // Construir dispatch simulado para reutilizar el flujo original (pasos 2-13)
        var dispatch = new DispatchResult(
            ExistingConversationId: null,
            SelectedAgentId: agentId,
            Intent: decision.Intent,
            IsExistingSession: false,
            IsCampaignContact: false,
            CampaignId: null);

        // Intentar recuperar sesión existente de Redis para continuidad
        try
        {
            var existingSession = await sessions.GetAsync(cmd.TenantId, cmd.FromPhone, ct);
            if (existingSession is not null)
            {
                dispatch = dispatch with
                {
                    ExistingConversationId = existingSession.ConversationId,
                    IsExistingSession = true,
                    CampaignId = existingSession.CampaignId
                };
            }
        }
        catch { /* Redis no disponible */ }

        // ── Reutilizar flujo original desde paso 2 en adelante ──
        return await ExecuteStandardFlow(cmd, dispatch, tenant, isBrainControlled: true, ct,
            brainCampaignTemplateId: brainCampaignTemplateId);
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
        Guid? brainCampaignTemplateId = null)
    {
        // ── 2. OBTENER O CREAR CONVERSACIÓN ──────────────
        Conversation conversation;
        if (dispatch.IsExistingSession && dispatch.ExistingConversationId.HasValue)
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
        var recentHistorySnapshot = conversation.Messages?
            .OrderByDescending(m => m.SentAt).Take(10).OrderBy(m => m.SentAt).ToList() ?? [];

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

            if (agent is not null)
            {
                if (conversation.ActiveAgentId != agent.Id)
                    conversation.ActiveAgentId = agent.Id;

                // ── CEREBRO: usar el SystemPrompt del maestro de campaña, no el del AgentDefinition ──
                // Cada maestro tiene su propio prompt aunque compartan el mismo agente IA.
                if (isBrainControlled && brainCampaignTemplateId.HasValue)
                {
                    var brainTemplate = await agents.GetCampaignTemplateByIdAsync(brainCampaignTemplateId.Value, ct);
                    if (brainTemplate is not null && !string.IsNullOrEmpty(brainTemplate.SystemPrompt))
                    {
                        agent.SystemPrompt = brainTemplate.SystemPrompt;
                    }
                }

                var tenantApiKey = tenant.LlmApiKey;

                // Horario de atención
                List<int>? attentionDays = null;
                string? attentionStart = null, attentionEnd = null;

                // Resolver CampaignTemplate: del Cerebro o de la conversación
                var templateIdToLoad = brainCampaignTemplateId ?? null;
                if (templateIdToLoad is null && conversation.CampaignId.HasValue)
                {
                    var campaign = await conversations.GetCampaignAsync(conversation.CampaignId.Value, ct);
                    templateIdToLoad = campaign?.CampaignTemplateId;
                }
                if (templateIdToLoad.HasValue)
                {
                    var tmpl = await agents.GetCampaignTemplateByIdAsync(templateIdToLoad.Value, ct);
                    if (tmpl is not null)
                    {
                        attentionDays = tmpl.AttentionDays;
                        attentionStart = tmpl.AttentionStartTime;
                        attentionEnd = tmpl.AttentionEndTime;
                    }
                }

                var clientContext = new Dictionary<string, string>
                {
                    ["nombre"] = cmd.ClientName ?? conversation.ClientName ?? "Cliente",
                    ["telefono"] = cmd.FromPhone
                };
                if (dispatch.IsCampaignContact && conversation.PolicyNumber is not null)
                    clientContext["poliza"] = conversation.PolicyNumber;

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

        // Actualizar sesión Redis
        try
        {
            await sessions.SetAsync(cmd.TenantId, cmd.FromPhone, new SessionState(
                conversation.Id,
                conversation.ActiveAgentId ?? dispatch.SelectedAgentId ?? Guid.Empty,
                agentResponse?.DetectedIntent ?? dispatch.Intent,
                dispatch.CampaignId,
                conversation.IsHumanHandled,
                DateTime.UtcNow
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
}
