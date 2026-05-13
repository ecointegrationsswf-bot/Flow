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
        // Validación: si Brain está deshabilitado, una línea WhatsApp solo
        // puede atender a UN agente activo. Esto es lo que permite que el
        // routing por línea (ProcessIncomingMessageHandler) sea determinista.
        // En Brain enabled la línea puede compartirse — el Cerebro decide.
        var collision = await ValidateLineNotSharedAsync(
            dto.WhatsAppLineId, dto.IsActive, excludeAgentId: null, ct);
        if (collision is not null) return collision;

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

        var collision = await ValidateLineNotSharedAsync(
            dto.WhatsAppLineId, dto.IsActive, excludeAgentId: id, ct);
        if (collision is not null) return collision;

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
        var tenantId = tenantCtx.TenantId;
        var agent = await db.AgentDefinitions
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);

        if (agent is null) return NotFound();

        // Verificar si el agente está vinculado a algún Maestro de Campaña
        var linkedTemplates = await db.CampaignTemplates
            .Where(t => t.AgentDefinitionId == id && t.TenantId == tenantId)
            .Select(t => t.Name)
            .ToListAsync(ct);

        if (linkedTemplates.Count > 0)
        {
            var names = string.Join(", ", linkedTemplates.Select(n => $"\"{n}\""));
            return Conflict(new
            {
                error = $"Este agente está vinculado a {linkedTemplates.Count} maestro(s) de campaña: {names}. Desvincula el agente de esos maestros antes de eliminarlo."
            });
        }

        db.AgentDefinitions.Remove(agent);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Si el tenant tiene BrainEnabled=false y el agente que estamos guardando
    /// está activo + apunta a una línea, valida que NINGÚN otro agente activo
    /// del mismo tenant tenga esa línea. Retorna ConflictObjectResult si choca,
    /// null si todo OK.
    /// </summary>
    private async Task<IActionResult?> ValidateLineNotSharedAsync(
        Guid? whatsAppLineId, bool isActive, Guid? excludeAgentId, CancellationToken ct)
    {
        // Solo aplica cuando el agente queda activo Y tiene línea asignada.
        if (!isActive || !whatsAppLineId.HasValue) return null;

        var tenantId = tenantCtx.TenantId;

        // En Brain enabled se permite compartir línea entre agentes.
        var brainEnabled = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.BrainEnabled)
            .FirstOrDefaultAsync(ct);
        if (brainEnabled) return null;

        // Buscar otro agente activo del mismo tenant que ya use esa línea.
        var conflictQuery = db.AgentDefinitions
            .Include(a => a.WhatsAppLine)
            .Where(a => a.TenantId == tenantId
                     && a.IsActive
                     && a.WhatsAppLineId == whatsAppLineId);

        if (excludeAgentId.HasValue)
            conflictQuery = conflictQuery.Where(a => a.Id != excludeAgentId.Value);

        var existing = await conflictQuery.FirstOrDefaultAsync(ct);
        if (existing is null) return null;

        return Conflict(new
        {
            error = "line_already_assigned",
            message = $"La línea WhatsApp \"{existing.WhatsAppLine?.DisplayName ?? "(sin nombre)"}\" "
                    + $"ya está asignada al agente \"{existing.Name}\". "
                    + "En el plan básico una línea solo puede atender a un agente activo. "
                    + "Para usar la misma línea con varios agentes activa el Cerebro.",
            conflictingAgentId = existing.Id,
            conflictingAgentName = existing.Name,
        });
    }
}
