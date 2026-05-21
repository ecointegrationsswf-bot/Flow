using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Gestión de acciones a nivel de tenant. Permite al usuario del tenant ver
/// las acciones disponibles y configurar el DefaultWebhookContract (contrato
/// webhook default que heredan todos los maestros de campaña del tenant).
///
/// Las definiciones de acciones (Name, RequiresWebhook, etc.) las gestiona
/// el super_admin desde /api/admin/actions. Este controller solo expone la
/// configuración del webhook default por tenant.
/// </summary>
[ApiController]
[Route("api/tenant-actions")]
[Authorize]
public class TenantActionsController(ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    /// <summary>
    /// Lista las acciones activas visibles para este tenant — las asignadas por el
    /// super admin (Tenant.AssignedActionIds), con su estado de configuración webhook.
    ///
    /// AISLAMIENTO por tenant: cuando una acción asignada es global y existe un clon
    /// tenant-specific con el mismo Name, devolvemos el contract/triggerConfig del
    /// CLON (no del global). Así el tenant ve y edita SU contrato, sin afectar a
    /// otros tenants que tengan la misma acción asignada.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedActionIds })
            .FirstOrDefaultAsync(ct);

        var assignedIds = tenant?.AssignedActionIds ?? [];
        if (assignedIds.Count == 0)
            return Ok(Array.Empty<object>());

        // Cargar las acciones asignadas (pueden ser globales o tenant-specific del tenant).
        var assignedActions = await db.ActionDefinitions
            .Where(a => a.IsActive && assignedIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        if (assignedActions.Count == 0)
            return Ok(Array.Empty<object>());

        // Cargar TODOS los clones tenant-specific del tenant con los Names asignados
        // en UN solo round-trip. Usamos esto para sobreescribir contract/triggerConfig
        // cuando la fila asignada es global y existe un clon.
        var assignedNames = assignedActions.Select(a => a.Name).Distinct().ToList();
        var clonesByName = await db.ActionDefinitions
            .Where(a => a.TenantId == tenantId && a.IsActive && assignedNames.Contains(a.Name))
            .ToDictionaryAsync(a => a.Name, ct);

        var result = assignedActions.Select(a =>
        {
            // Si la asignada es global y existe clon tenant-specific con mismo Name,
            // tomamos contract/triggerConfig del clon (el resolver runtime también lo prefiere).
            var effective = (a.TenantId is null && clonesByName.TryGetValue(a.Name, out var clone))
                ? clone
                : a;
            return new
            {
                a.Id,
                a.Name,
                a.Description,
                a.RequiresWebhook,
                a.SendsEmail,
                a.SendsSms,
                a.IsDelinquencyDownload,
                DefaultWebhookContract = effective.DefaultWebhookContract,
                DefaultTriggerConfig   = effective.DefaultTriggerConfig,
                HasWebhookContract     = effective.DefaultWebhookContract != null,
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// DESHABILITADO (mayo-2026): el contrato del webhook lo gestiona EXCLUSIVAMENTE
    /// el super admin desde "/api/admin/tenants/{id}/actions/{actionId}/webhook-contract".
    /// El tenant solo puede CONSULTAR el contrato desde el panel del cliente, no
    /// modificarlo. Si tu UI todavía consume este endpoint, actualizá a llamar al
    /// admin endpoint o quitá la opción del frontend del tenant.
    /// </summary>
    [HttpPut("{id:guid}/webhook-contract")]
    public IActionResult UpdateWebhookContract(Guid id, [FromBody] UpdateWebhookContractRequest req)
    {
        _ = id; _ = req;
        return StatusCode(403, new
        {
            error = "El contrato del webhook lo configura el administrador. " +
                    "Solicítale al super admin que lo edite desde el panel de cliente."
        });
    }

    public record UpdateWebhookContractRequest(string? Contract);
}
