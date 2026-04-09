using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Brain;

/// <summary>
/// Orquestador central del Cerebro.
/// Aplica reglas de negocio en orden determinista y devuelve una decisión.
///
/// Reglas (en orden):
/// 1. ¿Estado HumanClosed? → rechazar
/// 2. ¿Estado Escalated_Human? → rechazar
/// 3. ¿ClassifierService dice shouldEscalate? → escalar
/// 4. ¿Campaña activa + desvío? → aplicar OutOfContextPolicy
/// 5. Rutear al agentSlug clasificado
/// </summary>
public class BrainService(
    ISessionStore sessions,
    IClassifierService classifier,
    IAgentRegistry registry,
    AgentFlowDbContext db) : IBrainService
{
    private const double EscalateConfidenceThreshold = 0.2;

    public async Task<BrainDecision> RouteAsync(BrainRequest request, CancellationToken ct = default)
    {
        // ── 1. Cargar sesión existente ──
        var session = await LoadSessionAsync(request.TenantId, request.ContactId, ct);

        // ── 2. Estados terminales — el Cerebro no interfiere ──
        if (session?.BrainState == BrainSessionState.HumanClosed)
        {
            return new BrainDecision(
                session.ActiveAgentSlug ?? "",
                "closed",
                BrainSessionState.HumanClosed,
                false,
                "Esta conversación fue cerrada por un ejecutivo.");
        }

        if (session?.BrainState == BrainSessionState.Escalated_Human)
        {
            return new BrainDecision(
                session.ActiveAgentSlug ?? "",
                "escalated",
                BrainSessionState.Escalated_Human,
                false);
        }

        // ── 3. Cargar agentes del tenant ──
        var agents = await registry.GetAgentsAsync(request.TenantId, ct);
        if (agents.Count == 0)
        {
            return new BrainDecision("", "no_agents", BrainSessionState.Active_AI, false,
                "No hay agentes configurados para este tenant.");
        }

        // ── 4. Clasificar intención ──
        var history = session?.IntentHistory ?? [];
        var classification = await classifier.ClassifyAsync(new ClassifierInput(
            request.TenantId,
            request.Message,
            history.ToList(),
            agents,
            session?.ActiveAgentSlug,
            session?.ActiveCampaignId.HasValue == true
                ? await GetCampaignNameAsync(session.ActiveCampaignId.Value, ct)
                : null
        ), ct);

        // ── 5. ¿Escalar a humano? ──
        if (classification.ShouldEscalate ||
            classification.Confidence < EscalateConfidenceThreshold)
        {
            var escalatedSession = CreateOrUpdateSession(session, request,
                BrainSessionState.Escalated_Human, classification, agents);
            escalatedSession = escalatedSession with { EscalatedAt = DateTime.UtcNow };
            await SaveSessionAsync(request.TenantId, request.ContactId, escalatedSession, ct);

            return new BrainDecision(
                classification.AgentSlug,
                classification.Intent,
                BrainSessionState.Escalated_Human,
                false);
        }

        // ── 6. ¿Campaña activa + desvío? → aplicar OutOfContextPolicy ──
        var campaignId = request.CampaignId ?? session?.ActiveCampaignId;
        if (campaignId.HasValue && session?.ActiveAgentSlug is not null
            && classification.AgentSlug != session.ActiveAgentSlug)
        {
            var policy = await GetOutOfContextPolicyAsync(campaignId.Value, ct);

            if (policy == OutOfContextPolicy.Contain)
            {
                // Mantener agente de campaña — ignorar clasificación
                var containSession = CreateOrUpdateSession(session, request,
                    BrainSessionState.Active_Campaign, classification, agents,
                    overrideSlug: session.ActiveAgentSlug);
                await SaveSessionAsync(request.TenantId, request.ContactId, containSession, ct);

                return new BrainDecision(
                    session.ActiveAgentSlug,
                    classification.Intent,
                    BrainSessionState.Active_Campaign,
                    false);
            }
            // Policy = Transfer → continuar con el agente clasificado (paso 7)
        }

        // ── 7. Routing normal ──
        var targetState = campaignId.HasValue
            ? BrainSessionState.Active_Campaign
            : BrainSessionState.Active_AI;

        var newSession = CreateOrUpdateSession(session, request, targetState, classification, agents);
        await SaveSessionAsync(request.TenantId, request.ContactId, newSession, ct);

        return new BrainDecision(
            classification.AgentSlug,
            classification.Intent,
            targetState,
            classification.RequiresValidation);
    }

    // ── Helpers ──

    private async Task<SessionState?> LoadSessionAsync(Guid tenantId, string contactId, CancellationToken ct)
    {
        try { return await sessions.GetAsync(tenantId, contactId, ct); }
        catch { return null; }
    }

    private async Task SaveSessionAsync(Guid tenantId, string contactId, SessionState state, CancellationToken ct)
    {
        try { await sessions.SetAsync(tenantId, contactId, state, TimeSpan.FromHours(72), ct); }
        catch { /* Redis no disponible */ }
    }

    private static SessionState CreateOrUpdateSession(
        SessionState? existing,
        BrainRequest request,
        BrainSessionState brainState,
        ClassificationResult classification,
        List<AgentEntry> agents,
        string? overrideSlug = null)
    {
        var slug = overrideSlug ?? classification.AgentSlug;
        var agentEntry = agents.FirstOrDefault(a => a.Slug == slug);

        // Actualizar historial de intenciones (máx 10)
        var intentHistory = existing?.IntentHistory?.ToList() ?? [];
        intentHistory.Add(classification.Intent);
        if (intentHistory.Count > 10)
            intentHistory = intentHistory.TakeLast(10).ToList();

        var origin = existing?.Origin ?? (request.CampaignId.HasValue
            ? SessionOrigin.Campaign
            : SessionOrigin.Inbound);

        return new SessionState(
            ConversationId: existing?.ConversationId ?? Guid.Empty,
            AgentId: agentEntry?.AgentDefinitionId ?? existing?.AgentId ?? Guid.Empty,
            AgentType: classification.Intent,
            CampaignId: existing?.CampaignId ?? request.CampaignId,
            IsHumanHandled: brainState == BrainSessionState.Escalated_Human,
            LastActivityAt: DateTime.UtcNow,
            BrainState: brainState,
            Origin: origin,
            ActiveCampaignId: request.CampaignId ?? existing?.ActiveCampaignId,
            ActiveAgentSlug: slug,
            IntentHistory: intentHistory,
            ValidationState: existing?.ValidationState,
            EscalatedAt: existing?.EscalatedAt
        );
    }

    private async Task<string?> GetCampaignNameAsync(Guid campaignId, CancellationToken ct)
    {
        return await db.Campaigns
            .Where(c => c.Id == campaignId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<OutOfContextPolicy> GetOutOfContextPolicyAsync(Guid campaignId, CancellationToken ct)
    {
        return await db.Campaigns
            .Where(c => c.Id == campaignId)
            .Select(c => c.OutOfContextPolicy)
            .FirstOrDefaultAsync(ct);
    }
}
