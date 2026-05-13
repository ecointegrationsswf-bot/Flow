using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Action Trigger Protocol — Capa 2: construcción del catálogo de acciones
/// disponibles para el agente (bloque Markdown + diccionario slug→TriggerConfig
/// para validación posterior).
///
/// Flujo interno:
///  1. Cache hit por (tenantId, campaignTemplateId) con TTL 5 min.
///  2. Feature flag Tenant.WebhookContractEnabled — si false, catálogo vacío.
///  3. Carga CampaignTemplate.ActionConfigs (JSON) y enumera sus keys (action IDs).
///  4. Para cada acción, ActionConfigReader deserializa TriggerConfig.
///  5. Filtra las que tienen TriggerConfig.HasMeaningfulContent() == true.
///  6. Carga slugs desde ActionDefinitions en una sola query.
///  7. Construye el bloque Markdown según §3.2-3.3 del Action Trigger Protocol.
///  8. Mide tamaño del bloque y loguea warning si excede el umbral.
///
/// El mismo ActionCatalog (texto + diccionario) se devuelve desde GetCatalogAsync
/// para que el handler pueda validar el tag [ACTION:slug] emitido por el agente.
/// </summary>
public class ActionPromptBuilder(
    AgentFlowDbContext db,
    ActionConfigReader configReader,
    IMemoryCache cache,
    ILogger<ActionPromptBuilder> logger) : IActionPromptBuilder
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const int LargeBlockCharThreshold = 8000; // ~2000 tokens estimado en español
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ── API pública ──────────────────────────────────────────────────

    public async Task<string> BuildAsync(
        Guid? campaignTemplateId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(campaignTemplateId, tenantId, ct);
        return catalog.Block;
    }

    public async Task<ActionCatalog> GetCatalogAsync(
        Guid? campaignTemplateId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (!campaignTemplateId.HasValue)
        {
            logger.LogDebug("[ActionPromptBuilder] Sin campaignTemplateId — tenant {TenantId}. Catálogo vacío.", tenantId);
            return ActionCatalog.Empty;
        }

        var cacheKey = $"atp:catalog:{tenantId}:{campaignTemplateId.Value}";
        if (cache.TryGetValue(cacheKey, out ActionCatalog? cached) && cached is not null)
        {
            logger.LogDebug("[ActionPromptBuilder] Cache hit para {CacheKey}", cacheKey);
            return cached;
        }

        var catalog = await LoadCatalogAsync(campaignTemplateId.Value, tenantId, ct);
        cache.Set(cacheKey, catalog, CacheTtl);
        return catalog;
    }

    // ── Carga real (no cacheada) ──────────────────────────────────────

    private async Task<ActionCatalog> LoadCatalogAsync(
        Guid campaignTemplateId,
        Guid tenantId,
        CancellationToken ct)
    {
        // ── 1. Feature flag por tenant ──
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.WebhookContractEnabled })
            .FirstOrDefaultAsync(ct);

        if (tenant is null || !tenant.WebhookContractEnabled)
        {
            logger.LogDebug("[ActionPromptBuilder] WebhookContractEnabled=false para tenant {TenantId}. Catálogo vacío.", tenantId);
            return ActionCatalog.Empty;
        }

        // ── 2. Cargar ActionConfigs + ActionIds del template ──
        var templateRow = await db.CampaignTemplates
            .Where(t => t.Id == campaignTemplateId && t.TenantId == tenantId)
            .Select(t => new { t.ActionConfigs, t.ActionIds })
            .FirstOrDefaultAsync(ct);

        // ── 3. Conjunto UNIÓN de action IDs: ActionConfigs keys + ActionIds ──
        // ActionConfigs keys = acciones con config de template (webhookUrl, inputSchema, etc.)
        // ActionIds = acciones seleccionadas en la UI (pueden no tener config de template)
        // La unión garantiza que acciones con solo DefaultTriggerConfig (sin config de template)
        // también sean consideradas.
        var allActionIds = new HashSet<Guid>();

        // Desde ActionIds (lista de acciones seleccionadas en la UI)
        if (templateRow?.ActionIds is { Count: > 0 } templateActionIds)
        {
            foreach (var id in templateActionIds)
                allActionIds.Add(id);
        }

        // Desde ActionConfigs JSON (acciones con configuración de template)
        if (!string.IsNullOrWhiteSpace(templateRow?.ActionConfigs))
        {
            try
            {
                using var doc = JsonDocument.Parse(templateRow.ActionConfigs);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (Guid.TryParse(prop.Name, out var aid))
                            allActionIds.Add(aid);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("[ActionPromptBuilder] Error parseando ActionConfigs de template {TemplateId}: {Message}",
                    campaignTemplateId, ex.Message);
            }
        }

        if (allActionIds.Count == 0)
        {
            logger.LogDebug("[ActionPromptBuilder] Template {TemplateId} sin acciones", campaignTemplateId);
            return ActionCatalog.Empty;
        }

        // ── 4. Cargar ActionDefinitions con defaults en una sola query ──
        var defs = await db.ActionDefinitions
            .Where(a => a.TenantId == tenantId && allActionIds.Contains(a.Id) && a.IsActive)
            .Select(a => new { a.Id, a.Name, a.DefaultTriggerConfig, a.DefaultWebhookContract })
            .ToListAsync(ct);

        var defById = defs.ToDictionary(x => x.Id);

        // ── 5. Para cada acción, resolver TriggerConfig con herencia (3 niveles) ──
        // Prioridad: template triggerConfig > DefaultWebhookContract.triggerConfig > DefaultTriggerConfig
        var configured = new List<(Guid ActionId, TriggerConfig Trigger)>();
        foreach (var aid in allActionIds)
        {
            TriggerConfig? tc = null;

            // Nivel 1: triggerConfig del template (override)
            if (!string.IsNullOrWhiteSpace(templateRow?.ActionConfigs))
            {
                var bundle = configReader.Read(templateRow.ActionConfigs, aid);
                if (bundle?.TriggerConfig is { } templateTc && templateTc.HasMeaningfulContent())
                    tc = templateTc;
            }

            // Nivel 2: triggerConfig embebido en DefaultWebhookContract
            if (tc is null && defById.TryGetValue(aid, out var def)
                && !string.IsNullOrWhiteSpace(def.DefaultWebhookContract))
            {
                try
                {
                    var contractBundle = JsonSerializer.Deserialize<ActionConfigBundleJson>(
                        def.DefaultWebhookContract, _jsonOpts);
                    if (contractBundle?.TriggerConfig is { } contractTc && contractTc.HasMeaningfulContent())
                        tc = contractTc;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("[ActionPromptBuilder] DefaultWebhookContract inválido para action {ActionId}: {Message}",
                        aid, ex.Message);
                }
            }

            // Nivel 3: fallback a DefaultTriggerConfig standalone (retrocompat)
            if (tc is null && defById.TryGetValue(aid, out var def2)
                && !string.IsNullOrWhiteSpace(def2.DefaultTriggerConfig))
            {
                try
                {
                    var defaultTc = JsonSerializer.Deserialize<TriggerConfig>(
                        def2.DefaultTriggerConfig, _jsonOpts);
                    if (defaultTc is not null && defaultTc.HasMeaningfulContent())
                        tc = defaultTc;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("[ActionPromptBuilder] DefaultTriggerConfig inválido para action {ActionId}: {Message}",
                        aid, ex.Message);
                }
            }

            if (tc is not null)
                configured.Add((aid, tc));
        }

        if (configured.Count == 0)
        {
            logger.LogDebug("[ActionPromptBuilder] Template {TemplateId}: ninguna acción tiene TriggerConfig (ni template ni default)", campaignTemplateId);
            return ActionCatalog.Empty;
        }

        // ── 6. Construir diccionario slug→TriggerConfig ──
        var bySlug = new Dictionary<string, TriggerConfig>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<(Guid ActionId, string Slug, TriggerConfig Trigger)>();

        foreach (var (aid, tc) in configured)
        {
            if (defById.TryGetValue(aid, out var def) && !string.IsNullOrWhiteSpace(def.Name))
            {
                bySlug[def.Name.ToUpperInvariant()] = tc;
                ordered.Add((aid, def.Name, tc));
            }
        }

        if (ordered.Count == 0)
        {
            logger.LogWarning("[ActionPromptBuilder] Template {TemplateId}: ninguna ActionDefinition activa para las acciones con trigger", campaignTemplateId);
            return ActionCatalog.Empty;
        }

        // ── 6. Construir el bloque ──
        var block = BuildBlock(ordered);

        // ── 7. Size check ──
        if (block.Length > LargeBlockCharThreshold)
        {
            logger.LogWarning(
                "[ActionPromptBuilder] Bloque grande ({Length} chars ~{Tokens} tokens estimados) para tenant {TenantId} template {TemplateId}. Considera reducir triggerExamples o dividir acciones.",
                block.Length, block.Length / 4, tenantId, campaignTemplateId);
        }

        logger.LogInformation(
            "[ActionPromptBuilder] Catálogo construido: {Count} acciones, {Length} chars para tenant {TenantId} template {TemplateId}",
            ordered.Count, block.Length, tenantId, campaignTemplateId);

        return new ActionCatalog
        {
            Block = block,
            BySlug = bySlug
        };
    }

    /// <summary>
    /// Construye el texto Markdown exacto que describe §3.2 del Action Trigger Protocol.
    /// </summary>
    private static string BuildBlock(List<(Guid ActionId, string Slug, TriggerConfig Trigger)> ordered)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## ACCIONES DISPONIBLES");
        sb.AppendLine("Tienes acceso a las siguientes acciones. Lee cada una con atención.");
        sb.AppendLine("REGLAS DE USO — obligatorias:");
        sb.AppendLine("1. NUNCA declares una acción sin haber confirmado todos sus campos requeridos.");
        sb.AppendLine("2. Si la intención del usuario es ambigua, pregunta antes de actuar.");
        sb.AppendLine("3. Declara SOLO una acción por turno.");
        sb.AppendLine("4. El bloque [ACTION] va SIEMPRE al final de tu respuesta y el cliente NO lo ve.");
        sb.AppendLine();

        foreach (var (_, slug, tc) in ordered)
        {
            sb.AppendLine($"### [{slug}]");
            sb.AppendLine($"Cuándo usar: {tc.Description}");

            if (tc.TriggerExamples is { Count: > 0 } examples)
            {
                sb.AppendLine("Ejemplos de frases del usuario:");
                foreach (var ex in examples)
                    sb.AppendLine($"  - \"{ex}\"");
            }

            var hasRequired = tc.RequiresConfirmation is { Count: > 0 };
            if (hasRequired)
            {
                var fields = string.Join(", ", tc.RequiresConfirmation!);
                sb.AppendLine($"Debes confirmar antes de ejecutar: {fields}");
                if (!string.IsNullOrWhiteSpace(tc.ClarificationPrompt))
                    sb.AppendLine($"Pregunta sugerida: \"{tc.ClarificationPrompt}\"");
            }
            else
            {
                sb.AppendLine("Puedes ejecutar de inmediato cuando detectes la intención.");
            }

            sb.AppendLine("Para ejecutar esta acción declara al final de tu respuesta:");
            sb.AppendLine($"  [ACTION:{slug}]");
            if (hasRequired)
            {
                foreach (var field in tc.RequiresConfirmation!)
                    sb.AppendLine($"  [PARAM:{field}=<valor confirmado>]");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
