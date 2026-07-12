using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Implementación del resolver de auto-encadenamiento.
///
/// Patrón:
///  1. Cargar la ActionDefinition por slug, priorizando el clon tenant-specific
///     (TenantId match) sobre la fila global (TenantId=NULL) — mismo patrón que
///     usan TenantActionsController.GetAll y CampaignTemplatesController.AvailableActions.
///  2. Deserializar DefaultWebhookContract y leer ChainRules.
///  3. Parsear rawResponseJson con JsonDocument.
///  4. Evaluar reglas en orden; devolver el slug de la primera que matchea.
///
/// MVP de operadores: solo "equals" (case-insensitive string comparison).
/// MVP de paths: dot-notation simple (ej: "status", "data.code"). No arrays.
/// </summary>
public class ActionChainResolver(
    AgentFlowDbContext db,
    ILogger<ActionChainResolver> logger) : IActionChainResolver
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<ChainDecision?> GetNextActionAsync(
        string executedSlug,
        Guid tenantId,
        string? rawResponseJson,
        CancellationToken ct,
        string? extraContextJson = null)
    {
        if (string.IsNullOrWhiteSpace(executedSlug) || string.IsNullOrWhiteSpace(rawResponseJson))
            return null;

        // 1-2. Cargar el bundle del contrato (per-tenant → global) y leer ChainRules.
        var bundle = await LoadBundleAsync(executedSlug, tenantId, ct);
        var rules = bundle?.ChainRules;
        if (rules is null || rules.Count == 0)
            return null;

        // 3. Construir el CONTEXTO de evaluación: el response del webhook es el RAÍZ
        //    (compat: `status`, `data.code`) y se MERGEAN los namespaces extra como
        //    propiedades top-level (ej: `llm`). Así una regla puede condicionar por la
        //    respuesta del webhook O por el resultado del LLM (`llm.intent`).
        var contextJson = MergeEvalContext(rawResponseJson, extraContextJson, executedSlug);
        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(contextJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning("[ChainResolver] Response no es JSON válido tras {Slug}: {Msg}",
                executedSlug, ex.Message);
            return null;
        }

        using (doc)
        {
            // 4. Evaluar en orden. Primer match gana (semántica switch/case).
            foreach (var rule in rules)
            {
                var cond = rule.When;
                bool hasCondition = !string.IsNullOrWhiteSpace(cond.Path)
                                    || (cond.AllOf is { Count: > 0 })
                                    || (cond.AnyOf is { Count: > 0 });
                if (!hasCondition)
                    continue;

                if (EvaluateCondition(doc.RootElement, cond))
                {
                    var nextSlug = rule.Then?.ActionSlug;
                    // Si NO hay next slug Y NO se pide regenerar reply, la regla es puramente
                    // documental ("esta respuesta termina acá") y devolvemos null para que el
                    // orquestador no haga nada extra.
                    if (string.IsNullOrWhiteSpace(nextSlug) && !rule.RegenerateReply)
                    {
                        logger.LogDebug("[ChainResolver] Match en {Slug} para path={Path} pero `then` es null y sin regenerate — rama terminal documentada",
                            executedSlug, rule.When.Path);
                        return null;
                    }
                    logger.LogInformation("[ChainResolver] {Slug} → Next={Next} Regenerate={Regen} (condición matcheó)",
                        executedSlug, nextSlug ?? "(none)", rule.RegenerateReply);
                    return new ChainDecision(
                        NextSlug: string.IsNullOrWhiteSpace(nextSlug) ? null : nextSlug,
                        SuccessMessageTemplate: rule.Then?.SuccessMessage,
                        RegenerateReply: rule.RegenerateReply);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resuelve un path tipo "status" o "data.code" sobre un JsonElement.
    /// Devuelve null si el path no existe o el valor es null.
    /// Coerce a string: bool/number → ToString, string → value, object/array → ToString crudo.
    /// </summary>
    private static string? ResolvePath(JsonElement root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;

            // TryGetProperty es case-sensitive — buscamos manualmente case-insensitive
            // porque algunos brokers devuelven `Status` y otros `status`.
            JsonElement next = default;
            bool found = false;
            foreach (var prop in current.EnumerateObject())
            {
                if (prop.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    next = prop.Value;
                    found = true;
                    break;
                }
            }
            if (!found) return null;
            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => current.ToString()
        };
    }

    /// <summary>
    /// Motor de flujos Fase 2: config de auth de una acción, leída del contrato
    /// (per-tenant → global). Si no hay contrato, "no requiere auth".
    /// </summary>
    public async Task<ActionAuthConfig> GetAuthConfigAsync(Guid tenantId, string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return new ActionAuthConfig(false, null, null);
        var bundle = await LoadBundleAsync(slug, tenantId, ct);
        if (bundle is null)
            return new ActionAuthConfig(false, null, null);
        return new ActionAuthConfig(bundle.RequiresAuth, bundle.AuthPolicy, bundle.AuthRequiredMessage);
    }

    /// <summary>
    /// Escalamiento robusto Fase D: tope de ejecuciones por conversación de una acción,
    /// leído del contrato (per-tenant → global). Sin contrato o sin el campo → sin límite.
    /// </summary>
    public async Task<ActionCallCap> GetCallCapAsync(Guid tenantId, string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return new ActionCallCap(null, null);
        var bundle = await LoadBundleAsync(slug, tenantId, ct);
        if (bundle is null)
            return new ActionCallCap(null, null);
        return new ActionCallCap(bundle.MaxCallsPerConversation, bundle.CallsExhaustedMessage);
    }

    /// <summary>
    /// Carga y deserializa el bundle del contrato de una acción: per-tenant
    /// (TenantActionContract) → fallback global (ActionDefinition.DefaultWebhookContract).
    /// Devuelve null si no hay contrato o no deserializa.
    /// </summary>
    private async Task<ActionConfigBundleJson?> LoadBundleAsync(string slug, Guid tenantId, CancellationToken ct)
    {
        var contractJson = await TenantActionContractLookup.ResolveContractJsonAsync(db, tenantId, slug, ct);
        if (string.IsNullOrWhiteSpace(contractJson))
        {
            var candidates = await db.Set<ActionDefinition>()
                .Where(a => a.IsActive && a.Name == slug && (a.TenantId == tenantId || a.TenantId == null))
                .ToListAsync(ct);
            var actionDef = candidates.FirstOrDefault(a => a.TenantId == tenantId)
                            ?? candidates.FirstOrDefault(a => a.TenantId == null);
            contractJson = actionDef?.DefaultWebhookContract;
        }
        if (string.IsNullOrWhiteSpace(contractJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ActionConfigBundleJson>(contractJson, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning("[ChainResolver] No pude deserializar contract de {Slug}: {Msg}", slug, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Mergea el response del webhook (raíz) con los namespaces extra (ej: `llm`)
    /// en un solo objeto JSON para la evaluación. Si el response no es un objeto,
    /// o el merge falla, devuelve el response crudo (compat: sin namespaces).
    /// </summary>
    private string MergeEvalContext(string responseJson, string? extraContextJson, string executedSlug)
    {
        if (string.IsNullOrWhiteSpace(extraContextJson))
            return responseJson;
        try
        {
            if (JsonNode.Parse(responseJson) is not JsonObject root)
                return responseJson; // response no-objeto → no se puede mergear namespaces
            if (JsonNode.Parse(extraContextJson) is JsonObject extra)
            {
                foreach (var kv in extra)
                {
                    // Reparentar con un clon (parse del ToJsonString) para no romper el árbol origen.
                    root[kv.Key] = kv.Value is null ? null : JsonNode.Parse(kv.Value.ToJsonString());
                }
            }
            return root.ToJsonString();
        }
        catch (Exception ex)
        {
            logger.LogWarning("[ChainResolver] No pude mergear contexto extra tras {Slug}: {Msg}",
                executedSlug, ex.Message);
            return responseJson;
        }
    }

    /// <summary>
    /// Evalúa una condición (hoja o compuesta) contra el contexto.
    ///  - AllOf (AND): todas deben matchear.  - AnyOf (OR): al menos una.
    ///  - Hoja: resuelve Path y aplica el operador.
    /// AllOf/AnyOf tienen prioridad sobre Path. Sin nada que evaluar → false.
    /// </summary>
    private static bool EvaluateCondition(JsonElement root, ChainCondition cond)
    {
        if (cond.AllOf is { Count: > 0 } allOf)
            return allOf.All(sub => EvaluateCondition(root, sub));
        if (cond.AnyOf is { Count: > 0 } anyOf)
            return anyOf.Any(sub => EvaluateCondition(root, sub));
        if (string.IsNullOrWhiteSpace(cond.Path))
            return false;
        var observed = ResolvePath(root, cond.Path);
        return Matches(observed, cond.Operator, cond.Value);
    }

    /// <summary>
    /// Evalúa el operador. Strings case-insensitive; numéricos vía decimal (invariante).
    /// Operador desconocido / no parseable → false (fail-safe: no encadena).
    /// Soporta: equals, notEquals, contains, startsWith, isNotNull, isNull, gt, gte, lt, lte.
    /// </summary>
    private static bool Matches(string? observed, string? op, string? expected)
    {
        op = (op ?? "equals").Trim().ToLowerInvariant();
        bool CiEq(string? a, string? b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        switch (op)
        {
            case "equals": return CiEq(observed, expected);
            case "notequals": return !CiEq(observed, expected);
            case "contains":
                return (observed ?? string.Empty).Contains(expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "startswith":
                return (observed ?? string.Empty).StartsWith(expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "isnotnull": return !string.IsNullOrEmpty(observed);
            case "isnull": return string.IsNullOrEmpty(observed);
            case "gt": case "gte": case "lt": case "lte":
                if (decimal.TryParse(observed, NumberStyles.Any, CultureInfo.InvariantCulture, out var o)
                    && decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var e))
                {
                    return op switch
                    {
                        "gt" => o > e,
                        "gte" => o >= e,
                        "lt" => o < e,
                        "lte" => o <= e,
                        _ => false
                    };
                }
                return false;
            default: return false;
        }
    }
}
