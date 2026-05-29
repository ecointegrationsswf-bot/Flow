using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Resuelve el contrato webhook PER-TENANT de una acción
/// (arquitectura Acción global → Tenant → Contrato; ver
/// docs/arquitectura-acciones-contratos.md).
///
/// Devuelve el <c>ContractJson</c> del <see cref="Domain.Entities.TenantActionContract"/>
/// para (acción global por slug, tenant) o null si ese tenant no tiene contrato
/// propio para la acción. El JSON tiene el mismo shape que
/// <c>ActionDefinition.DefaultWebhookContract</c> → los consumidores lo
/// deserializan con <c>ActionConfigBundleJson</c> sin cambios.
///
/// Es aditivo: si retorna null, el llamador usa su lógica de fallback existente
/// (DefaultWebhookContract global / template ActionConfigs legacy) → no rompe nada.
/// </summary>
public static class TenantActionContractLookup
{
    public static async Task<string?> ResolveContractJsonAsync(
        AgentFlowDbContext db, Guid tenantId, string actionSlug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actionSlug)) return null;

        // El "encabezado" es la acción GLOBAL por slug.
        var globalActionId = await db.ActionDefinitions
            .Where(a => a.TenantId == null && a.Name == actionSlug && a.IsActive)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (globalActionId is null) return null;

        try
        {
            var json = await db.TenantActionContracts
                .Where(c => c.ActionDefinitionId == globalActionId.Value
                         && c.TenantId == tenantId
                         && c.IsActive)
                .Select(c => c.ContractJson)
                .FirstOrDefaultAsync(ct);

            return string.IsNullOrWhiteSpace(json) ? null : json;
        }
        catch
        {
            // Defensa: la tabla TenantActionContracts podría no existir todavía en
            // algún ambiente (el guard de creación corre en el arranque del API,
            // NO del Worker). Si la consulta falla, devolvemos null para caer al
            // fallback (DefaultWebhookContract global / clon legacy) sin romper
            // los envíos. Una vez creada la tabla, este catch nunca se dispara.
            return null;
        }
    }
}
