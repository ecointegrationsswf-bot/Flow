using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record LabelDto(string Name, string Color, List<string> Keywords);

[ApiController]
[Route("api/labels")]
[Microsoft.AspNetCore.Authorization.Authorize]
public class LabelsController(ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    // Proyección sin navegaciones para evitar ciclos de serialización
    private static object Project(ConversationLabel l) => new
    {
        l.Id, l.TenantId, l.Name, l.Color, l.Keywords, l.IsActive, l.CreatedAt
    };

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var labels = await db.ConversationLabels
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.TenantId, l.Name, l.Color, l.Keywords, l.IsActive, l.CreatedAt })
            .ToListAsync(ct);

        return Ok(labels);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var label = await db.ConversationLabels
            .Where(l => l.Id == id && l.TenantId == tenantId)
            .Select(l => new { l.Id, l.TenantId, l.Name, l.Color, l.Keywords, l.IsActive, l.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (label is null) return NotFound();
        return Ok(label);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LabelDto dto, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized(new { error = "Tenant no identificado." });

        // Verificar duplicado
        var exists = await db.ConversationLabels
            .AnyAsync(l => l.TenantId == tenantId && l.Name == dto.Name, ct);
        if (exists)
            return Conflict(new { error = $"Ya existe una etiqueta con el nombre '{dto.Name}'." });

        var label = new ConversationLabel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = dto.Name,
            Color = dto.Color,
            Keywords = dto.Keywords ?? [],
            CreatedAt = DateTime.UtcNow,
        };

        db.ConversationLabels.Add(label);
        await db.SaveChangesAsync(ct);

        return Ok(Project(label));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LabelDto dto, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var label = await db.ConversationLabels
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);

        if (label is null) return NotFound();

        // Verificar duplicado de nombre (excluir la propia etiqueta)
        var duplicate = await db.ConversationLabels
            .AnyAsync(l => l.TenantId == tenantId && l.Name == dto.Name && l.Id != id, ct);
        if (duplicate)
            return Conflict(new { error = $"Ya existe otra etiqueta con el nombre '{dto.Name}'." });

        label.Name = dto.Name;
        label.Color = dto.Color;
        label.Keywords = dto.Keywords ?? [];

        await db.SaveChangesAsync(ct);
        return Ok(Project(label));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var label = await db.ConversationLabels
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);

        if (label is null) return NotFound();

        db.ConversationLabels.Remove(label);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
