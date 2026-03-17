using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record LabelDto(string Name, string Color, List<string> Keywords);

[ApiController]
[Route("api/labels")]
public class LabelsController(ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var labels = await db.ConversationLabels
            .Where(l => l.TenantId == tenantCtx.TenantId)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        return Ok(labels);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var label = await db.ConversationLabels
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantCtx.TenantId, ct);

        if (label is null) return NotFound();
        return Ok(label);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LabelDto dto, CancellationToken ct)
    {
        var label = new ConversationLabel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantCtx.TenantId,
            Name = dto.Name,
            Color = dto.Color,
            Keywords = dto.Keywords ?? [],
            CreatedAt = DateTime.UtcNow,
        };

        db.ConversationLabels.Add(label);
        await db.SaveChangesAsync(ct);

        return Ok(label);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LabelDto dto, CancellationToken ct)
    {
        var label = await db.ConversationLabels
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantCtx.TenantId, ct);

        if (label is null) return NotFound();

        label.Name = dto.Name;
        label.Color = dto.Color;
        label.Keywords = dto.Keywords ?? [];

        await db.SaveChangesAsync(ct);
        return Ok(label);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var label = await db.ConversationLabels
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantCtx.TenantId, ct);

        if (label is null) return NotFound();

        db.ConversationLabels.Remove(label);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
