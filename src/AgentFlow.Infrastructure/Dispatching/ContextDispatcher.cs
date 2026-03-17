using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.Dispatching;

/// <summary>
/// Implementacion del dispatcher de contexto.
/// Decide que agente maneja un mensaje: sesion activa -> campana activa -> clasificacion LLM.
/// TODO: implementar logica completa con Redis + BD + LLM.
/// </summary>
public class ContextDispatcher(ISessionStore sessionStore) : IContextDispatcher
{
    public async Task<DispatchResult> DispatchAsync(DispatchRequest request, CancellationToken ct = default)
    {
        // 1. Verificar si hay sesion activa en Redis/memoria
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

        // 2. TODO: buscar en BD si es contacto de campana activa
        // 3. TODO: clasificar intencion con LLM

        // Por ahora retorna resultado vacio (nuevo contacto sin clasificar)
        return new DispatchResult(
            ExistingConversationId: null,
            SelectedAgentId: null,
            Intent: "new",
            IsExistingSession: false,
            IsCampaignContact: false
        );
    }
}
