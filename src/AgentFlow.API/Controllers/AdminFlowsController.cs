using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Motor de flujos — Fase 4. CRUD de los flujos visuales (workflows) por tenant.
/// Solo super-admin. Persiste el grafo del lienzo (reactflow) como JSON. El backend
/// NO ejecuta el grafo todavía (eso es la Fase 3 / FlowEngine) — esto es autoría.
/// Aditivo: no toca ningún flujo existente del inbound.
/// </summary>
[ApiController]
[Route("api/admin/flows")]
[Authorize(Roles = "super_admin")]
public class AdminFlowsController(AgentFlowDbContext db) : ControllerBase
{
    public record BoundTemplateDto(Guid Id, string Name);
    public record FlowListItemDto(Guid Id, Guid TenantId, string Name, string? Description, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, List<BoundTemplateDto> BoundTemplates);
    public record FlowDetailDto(Guid Id, Guid TenantId, string Name, string? Description, string FlowJson, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
    public record CreateFlowRequest(Guid TenantId, string Name, string? Description, string? FlowJson);
    public record UpdateFlowRequest(string Name, string? Description, string? FlowJson, bool? IsActive);
    public record TemplateForBindDto(Guid Id, string Name, Guid? ActiveFlowId, bool IsPrimaryForAgent);
    public record BindFlowRequest(Guid CampaignTemplateId, Guid? FlowId);

    /// <summary>Lista los flujos de un tenant (sin el grafo, liviano) con sus maestros vinculados. Si tenantId es null, lista todos.</summary>
    [HttpGet]
    public async Task<ActionResult<List<FlowListItemDto>>> List([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var q = db.TenantFlows.AsNoTracking();
        if (tenantId is { } tid)
            q = q.Where(f => f.TenantId == tid);

        var flows = await q
            .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
            .ToListAsync(ct);

        // Maestros que apuntan a cada flujo (el binding real que usa el motor).
        var flowIds = flows.Select(f => f.Id).ToList();
        var bindings = await db.CampaignTemplates.AsNoTracking()
            .Where(t => t.ActiveFlowId != null && flowIds.Contains(t.ActiveFlowId.Value))
            .Select(t => new { t.ActiveFlowId, t.Id, t.Name })
            .ToListAsync(ct);

        var items = flows.Select(f => new FlowListItemDto(
            f.Id, f.TenantId, f.Name, f.Description, f.IsActive, f.CreatedAt, f.UpdatedAt,
            bindings.Where(b => b.ActiveFlowId == f.Id)
                    .Select(b => new BoundTemplateDto(b.Id, b.Name)).ToList()
        )).ToList();

        return Ok(items);
    }

    /// <summary>Maestros del tenant con su flujo vinculado actual — para el selector de vínculo.</summary>
    [HttpGet("templates")]
    public async Task<ActionResult<List<TemplateForBindDto>>> TemplatesForBind([FromQuery] Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return BadRequest(new { error = "tenantId requerido" });
        var items = await db.CampaignTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new TemplateForBindDto(t.Id, t.Name, t.ActiveFlowId, t.IsPrimaryForAgent))
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>
    /// Vincula (o desvincula con FlowId=null) un flujo a un maestro. Este es EL binding que el
    /// motor de flujos consulta en runtime: con FlowId seteado, las conversaciones de ese maestro
    /// reciben el guion del flujo; en null vuelven al comportamiento clásico sin flujo.
    /// </summary>
    [HttpPut("bind")]
    public async Task<IActionResult> Bind([FromBody] BindFlowRequest req, CancellationToken ct)
    {
        var template = await db.CampaignTemplates.FirstOrDefaultAsync(t => t.Id == req.CampaignTemplateId, ct);
        if (template is null) return NotFound(new { error = "maestro no existe" });

        if (req.FlowId is { } fid)
        {
            var flow = await db.TenantFlows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fid, ct);
            if (flow is null) return NotFound(new { error = "flujo no existe" });
            if (flow.TenantId != template.TenantId)
                return BadRequest(new { error = "el flujo y el maestro pertenecen a tenants distintos" });
            template.ActiveFlowId = fid;
        }
        else
        {
            template.ActiveFlowId = null;
        }

        template.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { campaignTemplateId = template.Id, activeFlowId = template.ActiveFlowId });
    }

    /// <summary>Devuelve un flujo completo (con el grafo) para abrirlo en el lienzo.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FlowDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var f = await db.TenantFlows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound();
        return Ok(new FlowDetailDto(f.Id, f.TenantId, f.Name, f.Description, f.FlowJson, f.IsActive, f.CreatedAt, f.UpdatedAt));
    }

    /// <summary>Crea un flujo nuevo (lienzo vacío por defecto si no se manda FlowJson).</summary>
    [HttpPost]
    public async Task<ActionResult<FlowDetailDto>> Create([FromBody] CreateFlowRequest req, CancellationToken ct)
    {
        if (req.TenantId == Guid.Empty) return BadRequest(new { error = "tenantId requerido" });
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name requerido" });

        var tenantExists = await db.Tenants.AnyAsync(t => t.Id == req.TenantId, ct);
        if (!tenantExists) return BadRequest(new { error = "tenant no existe" });

        var flow = new TenantFlow
        {
            Id = Guid.NewGuid(),
            TenantId = req.TenantId,
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            FlowJson = string.IsNullOrWhiteSpace(req.FlowJson) ? "{\"nodes\":[],\"edges\":[]}" : req.FlowJson,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.TenantFlows.Add(flow);
        await db.SaveChangesAsync(ct);

        return Ok(new FlowDetailDto(flow.Id, flow.TenantId, flow.Name, flow.Description, flow.FlowJson, flow.IsActive, flow.CreatedAt, flow.UpdatedAt));
    }

    /// <summary>Actualiza un flujo (nombre, descripción, grafo, activo).</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<FlowDetailDto>> Update(Guid id, [FromBody] UpdateFlowRequest req, CancellationToken ct)
    {
        var flow = await db.TenantFlows.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (flow is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) flow.Name = req.Name.Trim();
        flow.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.FlowJson is not null) flow.FlowJson = req.FlowJson;
        if (req.IsActive is { } active) flow.IsActive = active;
        flow.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(new FlowDetailDto(flow.Id, flow.TenantId, flow.Name, flow.Description, flow.FlowJson, flow.IsActive, flow.CreatedAt, flow.UpdatedAt));
    }

    /// <summary>Elimina un flujo.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var flow = await db.TenantFlows.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (flow is null) return NotFound();
        db.TenantFlows.Remove(flow);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
