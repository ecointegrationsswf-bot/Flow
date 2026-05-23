using System.Text.Json;
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

    public async Task<string?> GetNextActionAsync(
        string executedSlug,
        Guid tenantId,
        string? rawResponseJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(executedSlug) || string.IsNullOrWhiteSpace(rawResponseJson))
            return null;

        // 1. Cargar la ActionDefinition correcta — preferir clon tenant-specific.
        var candidates = await db.Set<ActionDefinition>()
            .Where(a => a.IsActive && a.Name == executedSlug && (a.TenantId == tenantId || a.TenantId == null))
            .ToListAsync(ct);

        var actionDef = candidates.FirstOrDefault(a => a.TenantId == tenantId)
                        ?? candidates.FirstOrDefault(a => a.TenantId == null);

        if (actionDef is null || string.IsNullOrWhiteSpace(actionDef.DefaultWebhookContract))
            return null;

        // 2. Deserializar contract → leer ChainRules.
        List<ChainRule>? rules;
        try
        {
            var bundle = JsonSerializer.Deserialize<ActionConfigBundleJson>(
                actionDef.DefaultWebhookContract, JsonOpts);
            rules = bundle?.ChainRules;
        }
        catch (Exception ex)
        {
            logger.LogWarning("[ChainResolver] No pude deserializar contract de {Slug}: {Msg}",
                executedSlug, ex.Message);
            return null;
        }

        if (rules is null || rules.Count == 0)
            return null;

        // 3. Parsear el response JSON.
        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(rawResponseJson);
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
                if (string.IsNullOrWhiteSpace(rule.When.Path))
                    continue;

                var observed = ResolvePath(doc.RootElement, rule.When.Path);

                if (Matches(observed, rule.When.Operator, rule.When.Value))
                {
                    var nextSlug = rule.Then?.ActionSlug;
                    if (string.IsNullOrWhiteSpace(nextSlug))
                    {
                        logger.LogDebug("[ChainResolver] Match en {Slug} para path={Path} pero `then` es null (rama terminal documentada)",
                            executedSlug, rule.When.Path);
                        return null;
                    }
                    logger.LogInformation("[ChainResolver] {Slug} → {Next} via {Path}={Value}",
                        executedSlug, nextSlug, rule.When.Path, observed ?? "(null)");
                    return nextSlug;
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
    /// Evalúa la condición. MVP: solo `equals` (case-insensitive).
    /// Si el operator no se reconoce o falta, devuelve false (fail-safe: no encadena).
    /// </summary>
    private static bool Matches(string? observed, string? op, string? expected)
    {
        op = (op ?? string.Empty).Trim().ToLowerInvariant();
        return op switch
        {
            "equals" => string.Equals(observed ?? string.Empty, expected ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
