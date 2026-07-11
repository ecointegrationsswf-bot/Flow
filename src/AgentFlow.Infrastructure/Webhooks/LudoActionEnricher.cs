using System.Text.Json;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>Resultado del enriquecimiento. Si Success=false, UserMessage es la respuesta amable al cliente.</summary>
public sealed record LudoEnrichment(
    bool Success,
    string? UserMessage,
    WebhookEndpointConfig Endpoint,
    CollectedParams Params)
{
    public static LudoEnrichment Ok(WebhookEndpointConfig endpoint, CollectedParams p) => new(true, null, endpoint, p);
    public static LudoEnrichment Fail(string userMessage, WebhookEndpointConfig endpoint, CollectedParams p) => new(false, userMessage, endpoint, p);
}

/// <summary>
/// Integración Ludo CRM — Fase 4. Pre-paso GATED de las acciones de salida hacia Ludo
/// (solo corre si Tenant.LudoIntegrationEnabled y el slug es Ludo — cero impacto en el resto).
///
/// Para <c>mover_fase</c> hace las dos resoluciones que el LLM no debe hacer:
/// <list type="number">
///   <item><b>etapa → faseId</b>: traduce el nombre de etapa que emite el agente
///     ([PARAM:etapa=Calificado]) al faseId de Ludo vía StageLabelMap (clave estable).
///     Sin mapeo → fallback faseNombre=etapa (Ludo matchea por nombre, case-insensitive).</item>
///   <item><b>teléfono → oportunidadId</b>: GET /api/integration/prospecto?telefono= y
///     matchea la oportunidad ACTIVA (por objetivo si el agente lo dio; si no, la única/primera).
///     Reemplaza el placeholder {oportunidadId} de la URL del contrato.</item>
/// </list>
/// registrar_oportunidad y registrar_nota no necesitan enriquecimiento (upsert/lookup por
/// teléfono lo hace Ludo) — pasan directo.
/// </summary>
public interface ILudoActionEnricher
{
    Task<LudoEnrichment> EnrichAsync(
        Guid tenantId, string actionSlug, string contactPhone,
        CollectedParams collected, WebhookEndpointConfig endpoint, CancellationToken ct);
}

public class LudoActionEnricher(
    AgentFlowDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<LudoActionEnricher> logger) : ILudoActionEnricher
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const string NoOpportunityMessage =
        "Aún no encuentro una oportunidad registrada para avanzar. Primero registremos tu solicitud.";
    private const string LudoUnavailableMessage =
        "En este momento no puedo actualizar tu gestión. Lo intentaré de nuevo en unos minutos.";

    public async Task<LudoEnrichment> EnrichAsync(
        Guid tenantId, string actionSlug, string contactPhone,
        CollectedParams collected, WebhookEndpointConfig endpoint, CancellationToken ct)
    {
        // Solo mover_fase necesita resolución previa.
        if (!actionSlug.Equals(LudoIntegrationDefaults.MoverFaseSlug, StringComparison.OrdinalIgnoreCase))
            return LudoEnrichment.Ok(endpoint, collected);

        // ── 1. etapa → faseId (StageLabelMap) ──────────────────────────────────────
        var p = new CollectedParams { Values = new Dictionary<string, string?>(collected.Values, StringComparer.OrdinalIgnoreCase) };
        p.Values.TryGetValue("etapa", out var etapa);

        if (string.IsNullOrWhiteSpace(etapa)
            && !p.Values.ContainsKey("faseId") && !p.Values.ContainsKey("faseNombre"))
        {
            return LudoEnrichment.Fail(
                "¿A qué fase debería avanzar tu gestión? No logré determinarla.", endpoint, p);
        }

        if (!string.IsNullOrWhiteSpace(etapa))
        {
            var etapaTrim = etapa.Trim();
            var map = await db.StageLabelMaps
                .Where(m => m.TenantId == tenantId && m.IsActive && m.Nombre == etapaTrim)
                .Select(m => m.LudoStageId)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(map))
            {
                p.Values["faseId"] = map;         // id estable de Ludo (prioridad)
                p.Values["faseNombre"] = null;    // exactamente uno — Ludo prioriza el nombre si van ambos
            }
            else
            {
                p.Values["faseId"] = null;
                p.Values["faseNombre"] = etapaTrim; // fallback por nombre (case-insensitive en Ludo)
                logger.LogWarning("[LudoEnricher] Sin StageLabelMap para etapa '{Etapa}' tenant={TenantId} — fallback faseNombre.",
                    etapaTrim, tenantId);
            }
        }

        // ── 2. teléfono → oportunidadId (GET /prospecto) ───────────────────────────
        var idx = endpoint.WebhookUrl.IndexOf("/api/integration/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            logger.LogWarning("[LudoEnricher] URL de mover_fase sin /api/integration/: {Url}", endpoint.WebhookUrl);
            return LudoEnrichment.Fail(LudoUnavailableMessage, endpoint, p);
        }
        var baseUrl = endpoint.WebhookUrl[..idx];

        long? oportunidadId;
        try
        {
            oportunidadId = await ResolveOpportunityIdAsync(
                baseUrl, endpoint, contactPhone,
                p.Values.TryGetValue("objetivo", out var obj) ? obj : null, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[LudoEnricher] GET /prospecto falló tenant={TenantId}", tenantId);
            return LudoEnrichment.Fail(LudoUnavailableMessage, endpoint, p);
        }

        if (oportunidadId is null)
            return LudoEnrichment.Fail(NoOpportunityMessage, endpoint, p);

        // ── 3. Reemplazar el placeholder en la URL (copia — no mutamos el contrato) ──
        var resolvedUrl = endpoint.WebhookUrl.Replace(
            LudoIntegrationDefaults.OpportunityIdPlaceholder,
            oportunidadId.Value.ToString(),
            StringComparison.OrdinalIgnoreCase);

        var resolvedEndpoint = new WebhookEndpointConfig
        {
            WebhookUrl = resolvedUrl,
            WebhookMethod = endpoint.WebhookMethod,
            WebhookHeaders = endpoint.WebhookHeaders,
            AuthType = endpoint.AuthType,
            AuthValue = endpoint.AuthValue,
            ApiKeyHeaderName = endpoint.ApiKeyHeaderName,
            TimeoutSeconds = endpoint.TimeoutSeconds,
        };

        return LudoEnrichment.Ok(resolvedEndpoint, p);
    }

    /// <summary>
    /// GET /api/integration/prospecto?telefono={phone}. 204/sin prospecto → null.
    /// Entre las oportunidades ACTIVAS: matchea por objetivo si vino; si no, la primera
    /// (Ludo garantiza a lo sumo 1 activa por objetivo).
    /// </summary>
    private async Task<long?> ResolveOpportunityIdAsync(
        string baseUrl, WebhookEndpointConfig endpoint, string phone, string? objetivo, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ludo-integration");
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/integration/prospecto?telefono={Uri.EscapeDataString(phone)}");

        // Misma auth del contrato (token del tenant).
        if (endpoint.AuthType?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == true)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {endpoint.AuthValue}");
        else if (!string.IsNullOrWhiteSpace(endpoint.AuthValue))
            req.Headers.TryAddWithoutValidation(
                string.IsNullOrWhiteSpace(endpoint.ApiKeyHeaderName) ? "X-Api-Key" : endpoint.ApiKeyHeaderName,
                endpoint.AuthValue);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, endpoint.TimeoutSeconds)));

        using var resp = await client.SendAsync(req, cts.Token);
        if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null; // prospecto no existe

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cts.Token);
        var parsed = JsonSerializer.Deserialize<LudoEnvelope<LudoProspecto>>(body, JsonOpts);
        var ops = parsed?.Data?.Oportunidades;
        if (ops is null || ops.Count == 0) return null;

        var activas = ops.Where(o => o.Activa).ToList();
        if (activas.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(objetivo))
        {
            var match = activas.FirstOrDefault(o =>
                string.Equals(o.Objetivo, objetivo, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.OportunidadId;
        }

        return activas[0].OportunidadId;
    }

    // ── DTOs mínimos del envoltorio de Ludo ─────────────────────────────────────────
    private sealed class LudoEnvelope<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
    }

    private sealed class LudoProspecto
    {
        public long ProspectoId { get; set; }
        public List<LudoOportunidad>? Oportunidades { get; set; }
    }

    private sealed class LudoOportunidad
    {
        public long OportunidadId { get; set; }
        public string? Objetivo { get; set; }
        public bool Activa { get; set; }
    }
}
