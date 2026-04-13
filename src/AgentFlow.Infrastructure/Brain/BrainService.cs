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
/// 3. ¿Estado Pending_Validation? → continuar flujo de validación
/// 4. ¿ClassifierService dice requiresValidation? → iniciar flujo
/// 5. ¿ClassifierService dice shouldEscalate? → escalar
/// 6. ¿Campaña activa + desvío? → aplicar OutOfContextPolicy
/// 7. Rutear al agentSlug clasificado
/// </summary>
public class BrainService(
    ISessionStore sessions,
    IClassifierService classifier,
    IAgentRegistry registry,
    IValidationService validation,
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

        // ── 3. ¿Flujo de validación en curso? ──
        if (session?.BrainState == BrainSessionState.Pending_Validation
            && session.ValidationState is not null)
        {
            return await HandleValidationFlowAsync(request, session, ct);
        }

        // ── 3b. ¿Validation_Failed? — permitir reintento o escalar ──
        if (session?.BrainState == BrainSessionState.Validation_Failed)
        {
            // El cliente respondió después de un fallo — reiniciar como Active_AI
            var resetSession = session with
            {
                BrainState = BrainSessionState.Active_AI,
                ValidationState = null
            };
            await SaveSessionAsync(request.TenantId, request.ContactId, resetSession, ct);
            session = resetSession;
        }

        // ── 4. Cargar agentes del tenant ──
        var agents = await registry.GetAgentsAsync(request.TenantId, ct);
        if (agents.Count == 0)
        {
            return new BrainDecision("", "no_agents", BrainSessionState.Active_AI, false,
                "No hay agentes configurados para este tenant.");
        }

        // ── 5. Clasificar intención ──
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

        // ── 6. ¿Requiere validación de identidad? → iniciar flujo C ──
        if (classification.RequiresValidation)
        {
            var campaignTemplateId = await GetCampaignTemplateIdAsync(
                request.CampaignId ?? session?.ActiveCampaignId, ct);

            var flow = await validation.StartFlowAsync(
                request.TenantId, campaignTemplateId, classification.Intent, ct);

            if (!flow.IsComplete && flow.MessageToClient is not null)
            {
                // Hay preguntas configuradas — entrar en modo validación
                var valSession = CreateOrUpdateSession(session, request,
                    BrainSessionState.Pending_Validation, classification, agents);
                valSession = valSession with { ValidationState = flow.State };
                await SaveSessionAsync(request.TenantId, request.ContactId, valSession, ct);

                return new BrainDecision(
                    classification.AgentSlug,
                    classification.Intent,
                    BrainSessionState.Pending_Validation,
                    true,
                    flow.MessageToClient);
            }
            // Sin preguntas configuradas → continuar routing normal
        }

        // ── 7. ¿Escalar a humano? ──
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

        // ── 8. ¿Campaña activa + desvío? → aplicar OutOfContextPolicy ──
        var campaignId = request.CampaignId ?? session?.ActiveCampaignId;
        if (campaignId.HasValue && session?.ActiveAgentSlug is not null
            && classification.AgentSlug != session.ActiveAgentSlug)
        {
            var policy = await GetOutOfContextPolicyAsync(campaignId.Value, ct);

            if (policy == OutOfContextPolicy.Contain)
            {
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
        }

        // ── 9. Routing normal ──
        var targetState = campaignId.HasValue
            ? BrainSessionState.Active_Campaign
            : BrainSessionState.Active_AI;

        var newSession = CreateOrUpdateSession(session, request, targetState, classification, agents);
        await SaveSessionAsync(request.TenantId, request.ContactId, newSession, ct);

        return new BrainDecision(
            classification.AgentSlug,
            classification.Intent,
            targetState,
            false);
    }

    // ── Flujo de validación (Escenario C) ──

    private async Task<BrainDecision> HandleValidationFlowAsync(
        BrainRequest request, SessionState session, CancellationToken ct)
    {
        var flow = await validation.ContinueFlowAsync(session.ValidationState!, request.Message, ct);

        if (flow.ReadyToValidate)
        {
            // Todas las preguntas respondidas → llamar webhook
            var campaignTemplateId = await GetCampaignTemplateIdAsync(session.ActiveCampaignId, ct);
            var result = await validation.CallWebhookAsync(
                request.TenantId, campaignTemplateId, flow.State, ct);

            if (result.IsValid)
            {
                // Validación exitosa → volver a Active_AI
                var validSession = session with
                {
                    BrainState = BrainSessionState.Active_AI,
                    ValidationState = null
                };
                await SaveSessionAsync(request.TenantId, request.ContactId, validSession, ct);

                return new BrainDecision(
                    session.ActiveAgentSlug ?? "",
                    session.ValidationState!.Intent,
                    BrainSessionState.Active_AI,
                    false,
                    result.MessageToClient);
            }
            else
            {
                // Validación fallida
                var failedSession = session with
                {
                    BrainState = BrainSessionState.Validation_Failed,
                    ValidationState = null
                };
                await SaveSessionAsync(request.TenantId, request.ContactId, failedSession, ct);

                return new BrainDecision(
                    session.ActiveAgentSlug ?? "",
                    session.ValidationState!.Intent,
                    BrainSessionState.Validation_Failed,
                    false,
                    result.MessageToClient ?? "No pudimos verificar su identidad. Un ejecutivo lo atenderá.");
            }
        }

        // Quedan preguntas pendientes — seguir preguntando
        var updatedSession = session with { ValidationState = flow.State };
        await SaveSessionAsync(request.TenantId, request.ContactId, updatedSession, ct);

        return new BrainDecision(
            session.ActiveAgentSlug ?? "",
            session.ValidationState!.Intent,
            BrainSessionState.Pending_Validation,
            true,
            flow.MessageToClient);
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
            EscalatedAt: existing?.EscalatedAt,
            // Propagar campos del Webhook Contract System / Action Trigger Protocol
            // desde la sesión previa para que no se pierdan al reclasificar.
            ActionContext: existing?.ActionContext,
            LastActionResult: existing?.LastActionResult
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

    private async Task<Guid?> GetCampaignTemplateIdAsync(Guid? campaignId, CancellationToken ct)
    {
        if (!campaignId.HasValue) return null;
        return await db.Campaigns
            .Where(c => c.Id == campaignId.Value)
            .Select(c => c.CampaignTemplateId)
            .FirstOrDefaultAsync(ct);
    }
}
