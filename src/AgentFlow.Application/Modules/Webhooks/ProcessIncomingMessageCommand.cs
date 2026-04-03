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
    IConversationNotifier notifier
) : IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>
{
    public async Task<ProcessIncomingMessageResult> Handle(
        ProcessIncomingMessageCommand cmd, CancellationToken ct)
    {
        // ── 1. DISPATCHER: ¿qué agente responde? ────────
        var dispatch = await dispatcher.DispatchAsync(new(
            cmd.TenantId, cmd.FromPhone, cmd.Message, cmd.Channel.ToString()), ct);

        // ── 2. OBTENER O CREAR CONVERSACIÓN ──────────────
        Conversation conversation;
        if (dispatch.IsExistingSession && dispatch.ExistingConversationId.HasValue)
        {
            conversation = (await conversations.GetByIdAsync(dispatch.ExistingConversationId.Value, ct))!;

            // Si la conversación estaba cerrada, sin respuesta o esperando al cliente,
            // reabrirla — el cliente acaba de responder, por lo tanto está activo.
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
                CampaignId = null,
                Status = ConversationStatus.Active,
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
            conversation = await conversations.CreateAsync(conversation, ct);
        }

        // ── 3. PERSISTIR MENSAJE ENTRANTE ────────────────
        // Capturar historial ANTES de agregar el nuevo mensaje para evitar duplicarlo
        // en el contexto del LLM (EF Core añade el nuevo mensaje a conversation.Messages
        // en cuanto se llama AddMessageAsync, lo que causaría que aparezca dos veces)
        var recentHistorySnapshot = conversation.Messages?
            .OrderByDescending(m => m.SentAt)
            .Take(10)
            .OrderBy(m => m.SentAt)
            .ToList() ?? [];

        // Si hay media, incluir la URL en el contenido para que quede registrado
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
        // Actualizar LastActivityAt ANTES de guardar para que la lista se actualice inmediatamente
        conversation.LastActivityAt = DateTime.UtcNow;
        await conversations.AddMessageAsync(inbound, ct);
        await conversations.SaveChangesAsync(ct);

        // Notificar al monitor que llegó un mensaje
        try
        {
            await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
            {
                Type = "inbound",
                ConversationId = conversation.Id,
                From = cmd.FromPhone,
                Body = cmd.Message,
                ClientName = cmd.ClientName,
                Timestamp = DateTime.UtcNow
            });
        }
        catch { /* SignalR no disponible — no es crítico */ }

        // ── 4. EJECUTAR AGENTE IA ────────────────────────
        AgentResponse? agentResponse = null;
        string replyText = "";

        // Solo ejecutar si no está manejado por humano y hay un agente asignado
        if (!conversation.IsHumanHandled)
        {
            // Resolver qué agente usar
            var agentId = dispatch.SelectedAgentId
                ?? conversation.ActiveAgentId;

            AgentDefinition? agent = null;
            if (agentId.HasValue)
                agent = await agents.GetByIdAsync(agentId.Value, ct);

            // Si no hay agente específico, usar el primero activo del tenant
            agent ??= await agents.GetFirstActiveByTenantAsync(cmd.TenantId, ct);

            if (agent is not null)
            {
                // Actualizar el agente activo en la conversación
                if (conversation.ActiveAgentId != agent.Id)
                {
                    conversation.ActiveAgentId = agent.Id;
                }

                // Usar el historial capturado ANTES de agregar el mensaje actual,
                // para evitar que el mismo mensaje aparezca dos veces en el contexto del LLM
                var recentHistory = recentHistorySnapshot;

                // Obtener API key del tenant para el LLM
                var tenant = await agents.GetTenantByIdAsync(cmd.TenantId, ct);
                var tenantApiKey = tenant?.LlmApiKey;

                // Construir contexto del cliente (datos de campaña si aplica)
                var clientContext = new Dictionary<string, string>
                {
                    ["nombre"] = cmd.ClientName ?? conversation.ClientName ?? "Cliente",
                    ["telefono"] = cmd.FromPhone
                };

                // Si es contacto de campaña, agregar datos de la póliza
                if (dispatch.IsCampaignContact && conversation.PolicyNumber is not null)
                {
                    clientContext["poliza"] = conversation.PolicyNumber;
                }

                // ── LLAMAR AL LLM ────────────────────────
                try
                {
                    agentResponse = await agentRunner.RunAsync(new AgentRunRequest(
                        Agent: agent,
                        Conversation: conversation,
                        IncomingMessage: cmd.Message,
                        RecentHistory: recentHistory,
                        ClientContext: clientContext,
                        TenantLlmApiKey: tenantApiKey,
                        // Pasar MediaUrl para imágenes Y documentos PDF
                        MediaUrl: (cmd.MediaType == "image" || cmd.MediaType == "document")
                            ? cmd.MediaUrl : null,
                        MediaType: cmd.MediaType
                    ), ct);

                    replyText = agentResponse.ReplyText;
                }
                catch (Exception ex)
                {
                    // LLM no disponible — respuesta de fallback
                    replyText = "Gracias por su mensaje. En este momento no puedo procesar su solicitud. Un ejecutivo se comunicará con usted a la brevedad.";
                    agentResponse = new AgentResponse(replyText, dispatch.Intent, 0, false, false, 0);
                    Console.WriteLine($"[AgentRunner] Error: {ex.Message}");
                }

                // ── 5. PERSISTIR RESPUESTA DEL AGENTE ────
                // Guardar en BD INMEDIATAMENTE para que aparezca en el monitor
                // sin esperar la confirmación de WhatsApp
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
                await conversations.SaveChangesAsync(ct);  // ← guardar ya, sin esperar WhatsApp

                // Notificar al monitor la respuesta del agente (antes de enviar por WhatsApp)
                try
                {
                    await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
                    {
                        Type = "outbound",
                        ConversationId = conversation.Id,
                        Body = replyText,
                        AgentName = agent.AvatarName ?? agent.Name,
                        Intent = agentResponse.DetectedIntent,
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch { /* SignalR no disponible */ }

                // ── 6. ENVIAR POR WHATSAPP ───────────────
                try
                {
                    var provider = await channelFactory.GetProviderAsync(cmd.TenantId, ct);
                    if (provider is not null)
                    {
                        var sendResult = await provider.SendMessageAsync(
                            new SendMessageRequest(cmd.FromPhone, replyText), ct);

                        outbound.ExternalMessageId = sendResult.ExternalMessageId;
                        outbound.Status = sendResult.Success
                            ? MessageStatus.Sent
                            : MessageStatus.Failed;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WhatsApp] Error enviando respuesta: {ex.Message}");
                    outbound.Status = MessageStatus.Failed;
                }

                // Si el agente detecta que debe escalar a humano
                if (agentResponse.ShouldEscalate)
                {
                    conversation.Status = ConversationStatus.EscalatedToHuman;
                    try
                    {
                        await notifier.NotifyEscalationAsync(
                            cmd.TenantId.ToString(), conversation.Id);
                    }
                    catch { }
                }

                // Si el agente detecta que debe cerrar la conversación
                if (agentResponse.ShouldClose)
                {
                    conversation.Status = ConversationStatus.Closed;
                    conversation.ClosedAt = DateTime.UtcNow;
                }
            }
        }

        // ── 7. ACTUALIZAR SESIÓN EN REDIS ────────────────
        try
        {
            await sessions.SetAsync(cmd.TenantId, cmd.FromPhone, new SessionState(
                conversation.Id,
                conversation.ActiveAgentId ?? dispatch.SelectedAgentId ?? Guid.Empty,
                agentResponse?.DetectedIntent ?? dispatch.Intent,
                null,
                conversation.IsHumanHandled,
                DateTime.UtcNow
            ), TimeSpan.FromHours(72), ct);
        }
        catch { /* Redis no disponible */ }

        // ── 8. PERSISTIR TODO ────────────────────────────
        conversation.LastActivityAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(ct);

        return new ProcessIncomingMessageResult(
            conversation.Id,
            replyText,
            agentResponse?.ShouldEscalate ?? false,
            agentResponse?.ShouldClose ?? false,
            agentResponse?.DetectedIntent ?? dispatch.Intent
        );
    }
}
