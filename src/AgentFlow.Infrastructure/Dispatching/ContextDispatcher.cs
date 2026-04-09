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
/// 2. CAMPAÑA ACTIVA (BD) — Si el teléfono está en una campaña en curso (fue subido
///    en un archivo Excel de morosos), lo atiende el agente de esa campaña.
///
/// 3. CONVERSACIÓN ABIERTA (BD) — Si hubo una conversación anterior que no se cerró
///    (ej: cliente dejó de responder ayer), la retomamos con el mismo agente.
///
/// 4. CONTACTO NUEVO — Si ninguna de las anteriores aplica, es un contacto nuevo.
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

        // ── Paso 2: Contacto de campaña activa ───────────
        // Buscar si el teléfono está en alguna campaña activa del tenant.
        // Si lo encontramos, usamos el agente de esa campaña.
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
