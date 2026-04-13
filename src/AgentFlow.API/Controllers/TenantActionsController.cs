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
    /// <summary>Lista las acciones activas del tenant con su estado de configuración webhook.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var actions = await db.ActionDefinitions
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Description,
                a.RequiresWebhook,
                a.SendsEmail,
                a.SendsSms,
                a.DefaultWebhookContract,
                a.DefaultTriggerConfig,
                HasWebhookContract = a.DefaultWebhookContract != null
            })
            .ToListAsync(ct);

        return Ok(actions);
    }

    /// <summary>
    /// Actualiza el DefaultWebhookContract de una acción del tenant.
    /// El contract es un JSON string con el bundle completo (URL, auth, schemas, trigger).
    /// </summary>
    [HttpPut("{id:guid}/webhook-contract")]
    public async Task<IActionResult> UpdateWebhookContract(
        Guid id, [FromBody] UpdateWebhookContractRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var action = await db.ActionDefinitions
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);

        if (action is null)
            return NotFound(new { error = "Acción no encontrada para este tenant." });

        action.DefaultWebhookContract = req.Contract;

        // Si el contract tiene triggerConfig, sincronizar DefaultTriggerConfig para retrocompat
        if (!string.IsNullOrWhiteSpace(req.Contract))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(req.Contract);
                if (doc.RootElement.TryGetProperty("triggerConfig", out var tc)
                    && tc.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    action.DefaultTriggerConfig = tc.GetRawText();
                }
            }
            catch { /* si el JSON es inválido, no sincronizar */ }
        }
        else
        {
            action.DefaultWebhookContract = null;
        }

        action.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new { action.Id, action.DefaultWebhookContract, action.DefaultTriggerConfig });
    }

    public record UpdateWebhookContractRequest(string? Contract);
}
