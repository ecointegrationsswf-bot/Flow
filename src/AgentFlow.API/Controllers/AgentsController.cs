using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record AgentDto(
    string Name,
    string Type,
    bool IsActive,
    string SystemPrompt,
    string? Tone,
    string Language,
    string? AvatarName,
    List<string> EnabledChannels,
    string? SendFrom,
    string? SendUntil,
    int MaxRetries,
    int RetryIntervalHours,
    int InactivityCloseHours,
    string? CloseConditionKeyword,
    string LlmModel,
    double Temperature,
    int MaxTokens,
    Guid? WhatsAppLineId = null
);

[ApiController]
// [Authorize(Roles = "Admin,Supervisor")] // TODO: habilitar cuando auth esté configurado
[Route("api/agents")]
public class AgentsController(ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var agents = await db.AgentDefinitions
            .Include(a => a.WhatsAppLine)
            .Where(a => a.TenantId == tenantCtx.TenantId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var result = agents.Select(a => new
        {
            a.Id, a.TenantId, a.Name, Type = a.Type.ToString(), a.IsActive,
            a.SystemPrompt, a.Tone, a.Language, a.AvatarName,
            EnabledChannels = a.EnabledChannels.Select(c => c.ToString()).ToList(),
            SendFrom = a.SendFrom?.ToString("HH:mm"),
            SendUntil = a.SendUntil?.ToString("HH:mm"),
            a.MaxRetries, a.RetryIntervalHours, a.InactivityCloseHours, a.CloseConditionKeyword,
            a.LlmModel, a.Temperature, a.MaxTokens, a.WhatsAppLineId,
            WhatsAppLineName = a.WhatsAppLine?.DisplayName,
            WhatsAppLinePhone = a.WhatsAppLine?.PhoneNumber,
            a.CreatedAt, a.UpdatedAt,
        });

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var agent = await db.AgentDefinitions
            .Include(a => a.WhatsAppLine)
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantCtx.TenantId, ct);

        if (agent is null) return NotFound();

        return Ok(new
        {
            agent.Id, agent.TenantId, agent.Name, Type = agent.Type.ToString(), agent.IsActive,
            agent.SystemPrompt, agent.Tone, agent.Language, agent.AvatarName,
            EnabledChannels = agent.EnabledChannels.Select(c => c.ToString()).ToList(),
            SendFrom = agent.SendFrom?.ToString("HH:mm"),
            SendUntil = agent.SendUntil?.ToString("HH:mm"),
            agent.MaxRetries, agent.RetryIntervalHours, agent.InactivityCloseHours, agent.CloseConditionKeyword,
            agent.LlmModel, agent.Temperature, agent.MaxTokens, agent.WhatsAppLineId,
            WhatsAppLineName = agent.WhatsAppLine?.DisplayName,
            WhatsAppLinePhone = agent.WhatsAppLine?.PhoneNumber,
            agent.CreatedAt, agent.UpdatedAt,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgentDto dto, CancellationToken ct)
    {
        var agent = new AgentDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantCtx.TenantId,
            Name = dto.Name,
            Type = Enum.Parse<AgentType>(dto.Type),
            IsActive = dto.IsActive,
            SystemPrompt = dto.SystemPrompt,
            Tone = dto.Tone,
            Language = dto.Language,
            AvatarName = dto.AvatarName,
            EnabledChannels = dto.EnabledChannels.Select(Enum.Parse<ChannelType>).ToList(),
            SendFrom = string.IsNullOrEmpty(dto.SendFrom) ? null : TimeOnly.Parse(dto.SendFrom),
            SendUntil = string.IsNullOrEmpty(dto.SendUntil) ? null : TimeOnly.Parse(dto.SendUntil),
            MaxRetries = dto.MaxRetries,
            RetryIntervalHours = dto.RetryIntervalHours,
            InactivityCloseHours = dto.InactivityCloseHours,
            CloseConditionKeyword = dto.CloseConditionKeyword,
            LlmModel = dto.LlmModel,
            Temperature = dto.Temperature,
            MaxTokens = dto.MaxTokens,
            WhatsAppLineId = dto.WhatsAppLineId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.AgentDefinitions.Add(agent);
        await db.SaveChangesAsync(ct);

        return Ok(agent);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AgentDto dto, CancellationToken ct)
    {
        var agent = await db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantCtx.TenantId, ct);

        if (agent is null) return NotFound();

        agent.Name = dto.Name;
        agent.Type = Enum.Parse<AgentType>(dto.Type);
        agent.IsActive = dto.IsActive;
        agent.SystemPrompt = dto.SystemPrompt;
        agent.Tone = dto.Tone;
        agent.Language = dto.Language;
        agent.AvatarName = dto.AvatarName;
        agent.EnabledChannels = dto.EnabledChannels.Select(Enum.Parse<ChannelType>).ToList();
        agent.SendFrom = string.IsNullOrEmpty(dto.SendFrom) ? null : TimeOnly.Parse(dto.SendFrom);
        agent.SendUntil = string.IsNullOrEmpty(dto.SendUntil) ? null : TimeOnly.Parse(dto.SendUntil);
        agent.MaxRetries = dto.MaxRetries;
        agent.RetryIntervalHours = dto.RetryIntervalHours;
        agent.InactivityCloseHours = dto.InactivityCloseHours;
        agent.CloseConditionKeyword = dto.CloseConditionKeyword;
        agent.LlmModel = dto.LlmModel;
        agent.Temperature = dto.Temperature;
        agent.MaxTokens = dto.MaxTokens;
        agent.WhatsAppLineId = dto.WhatsAppLineId;
        agent.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(agent);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var agent = await db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantCtx.TenantId, ct);

        if (agent is null) return NotFound();

        db.AgentDefinitions.Remove(agent);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
