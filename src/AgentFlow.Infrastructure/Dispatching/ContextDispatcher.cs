using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Dispatching;

/// <summary>
/// Dispatcher de contexto — el "cerebro de routing" del sistema.
///
/// Cuando llega un mensaje, decide quién lo atiende siguiendo esta prioridad:
///
/// 1. REDIS (sesión activa) — Si el cliente habló hace poco, Redis tiene la sesión
///    con el agente asignado. Esto garantiza continuidad: si estaba hablando con el
///    agente de cobros, sigue con el de cobros.
///
/// 2. CONVERSACIÓN ABIERTA (BD) — Si existe una conversación NO cerrada para el
///    teléfono, retomarla. Cubre el caso típico: una campaña recién envió el welcome
///    (Conversation.Status=WaitingClient) y el cliente responde — el reply debe
///    ir a ESA conversación, sin importar si su Campaign ya pasó a Completed.
///
/// 3. CAMPAÑA ACTIVA (BD) — Si el teléfono está en una campaña en curso pero aún
///    no hay conversación (caso raro: inbound antes del primer outbound), lo
///    atiende el agente de esa campaña.
///
/// 4. CONVERSACIÓN PREVIA CERRADA (BD) — Si no hay nada activo pero hubo
///    conversaciones anteriores (incluso cerradas), retomar la más reciente.
///
/// 5. CONTACTO NUEVO — Si ninguna de las anteriores aplica, es un contacto nuevo.
///    El ProcessIncomingMessageHandler creará una conversación nueva.
/// </summary>
public class ContextDispatcher(
    ISessionStore sessionStore,
    AgentFlowDbContext db) : IContextDispatcher
{
    public async Task<DispatchResult> DispatchAsync(DispatchRequest request, CancellationToken ct = default)
    {
        // ── Paso 1: Sesión activa en Redis ───────────────
        // Redis almacena sesiones con TTL de 72 horas.
        // Si existe, el cliente habló recientemente y sabemos con qué agente.
        try
        {
            var session = await sessionStore.GetAsync(request.TenantId, request.FromPhone, ct);
            if (session is not null)
            {
                return new DispatchResult(
                    ExistingConversationId: session.ConversationId,
                    SelectedAgentId: session.AgentId,
                    Intent: session.AgentType,
                    IsExistingSession: true,
                    IsCampaignContact: session.CampaignId.HasValue
                );
            }
        }
        catch
        {
            // Redis no disponible — continuar con los siguientes pasos
        }

        // ── Paso 2: Conversación abierta ────────────────
        // PRIORIDAD: si existe una conversación NO cerrada para el teléfono,
        // retomarla — sin importar el estado IsActive de la campaña a la que
        // pertenece. Esto cubre el caso de una campaña que ya completó envío
        // (Status=Completed) pero su conversación quedó WaitingClient: el reply
        // del cliente debe linkearse a ESA conversación, no a otra campaña vieja
        // del mismo tenant que siga Running por accidente.
        var openConv = await db.Conversations
            .Where(c =>
                c.TenantId == request.TenantId
                && c.ClientPhone == request.FromPhone
                && c.Status != ConversationStatus.Closed)
            .OrderByDescending(c => c.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (openConv is not null)
        {
            var intentFromAgent = "cobros";
            if (openConv.ActiveAgentId.HasValue)
            {
                var agent = await db.Set<Domain.Entities.AgentDefinition>()
                    .FirstOrDefaultAsync(a => a.Id == openConv.ActiveAgentId, ct);
                if (agent is not null)
                    intentFromAgent = agent.Type.ToString().ToLower();
            }

            return new DispatchResult(
                ExistingConversationId: openConv.Id,
                SelectedAgentId: openConv.ActiveAgentId,
                Intent: intentFromAgent,
                IsExistingSession: true,
                IsCampaignContact: openConv.CampaignId.HasValue,
                CampaignId: openConv.CampaignId
            );
        }

        // ── Paso 3: Contacto de campaña activa ───────────
        // Si no hay conversación abierta, buscar si el teléfono está en alguna
        // campaña activa del tenant. Útil cuando la campaña aún no ha enviado
        // el primer mensaje (ej: mensaje entrante adelantándose al outbound).
        var campaignContact = await db.Set<Domain.Entities.CampaignContact>()
            .Include(cc => cc.Campaign)
            .Where(cc =>
                cc.Campaign!.TenantId == request.TenantId
                && cc.Campaign.IsActive
                && cc.PhoneNumber == request.FromPhone
                && cc.IsPhoneValid)
            .OrderByDescending(cc => cc.Campaign!.CreatedAt)  // la campaña más reciente primero
            .FirstOrDefaultAsync(ct);

        if (campaignContact?.Campaign is not null)
        {
            // Buscar si ya hay una conversación para este contacto EN ESTA CAMPAÑA.
            // Sin fallback a otras campañas para evitar mezclar datos de clientes.
            var existingConv = await db.Conversations
                .Where(c =>
                    c.TenantId == request.TenantId
                    && c.ClientPhone == request.FromPhone
                    && c.CampaignId == campaignContact.CampaignId
                    && c.Status != ConversationStatus.Closed)
                .FirstOrDefaultAsync(ct);

            return new DispatchResult(
                ExistingConversationId: existingConv?.Id,
                SelectedAgentId: campaignContact.Campaign.AgentDefinitionId,
                Intent: "cobros",  // campañas de cobros por defecto
                IsExistingSession: existingConv is not null,
                IsCampaignContact: true,
                CampaignId: campaignContact.CampaignId
            );
        }

        // ── Paso 3: Conversación existente en BD (abierta O cerrada) ───────────
        // Reutilizamos SIEMPRE la conversación más reciente del contacto.
        // Si estaba cerrada, el handler la reabrirá — así un contacto nunca
        // tiene más de una conversación visible en el monitor.
        var existingConversation = await db.Conversations
            .Where(c =>
                c.TenantId == request.TenantId
                && c.ClientPhone == request.FromPhone)
            .OrderByDescending(c => c.LastActivityAt)
            .FirstOrDefaultAsync(ct);

        if (existingConversation is not null)
        {
            var intent = "cobros"; // default
            if (existingConversation.ActiveAgentId.HasValue)
            {
                var agent = await db.Set<Domain.Entities.AgentDefinition>()
                    .FirstOrDefaultAsync(a => a.Id == existingConversation.ActiveAgentId, ct);
                if (agent is not null)
                    intent = agent.Type.ToString().ToLower();
            }

            return new DispatchResult(
                ExistingConversationId: existingConversation.Id,
                SelectedAgentId: existingConversation.ActiveAgentId,
                Intent: intent,
                IsExistingSession: true,
                IsCampaignContact: existingConversation.CampaignId.HasValue
            );
        }

        // ── Paso 4: Contacto nuevo (primera vez que escribe) ─────────────────
        return new DispatchResult(
            ExistingConversationId: null,
            SelectedAgentId: null,
            Intent: "new",
            IsExistingSession: false,
            IsCampaignContact: false
        );
    }
}
