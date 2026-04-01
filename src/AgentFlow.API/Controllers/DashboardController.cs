using AgentFlow.Application.Modules.Monitor;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(AgentFlowDbContext db, ITenantContext tenantCtx) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        // Conversaciones activas (no cerradas)
        var totalConversations = await db.Conversations
            .CountAsync(c => c.TenantId == tenantId
                && c.Status != ConversationStatus.Closed
                && c.Status != ConversationStatus.Unresponsive, ct);

        // Agentes activos (distintos agentes usados en conversaciones abiertas)
        var activeAgents = await db.Conversations
            .Where(c => c.TenantId == tenantId
                && c.Status != ConversationStatus.Closed
                && c.ActiveAgentId != null)
            .Select(c => c.ActiveAgentId)
            .Distinct()
            .CountAsync(ct);

        // Campañas activas
        var activeCampaigns = await db.Campaigns
            .CountAsync(c => c.TenantId == tenantId && c.IsActive, ct);

        // Escaladas a humano
        var escalatedCount = await db.Conversations
            .CountAsync(c => c.TenantId == tenantId
                && c.Status == ConversationStatus.EscalatedToHuman, ct);

        // Distribución por resultado de gestión (join con conversación para filtrar por tenant)
        var gestionByResult = await db.GestionEvents
            .Where(g => g.Conversation.TenantId == tenantId)
            .GroupBy(g => g.Result)
            .Select(g => new { Result = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        // Conversaciones recientes (últimas 10 con actividad)
        var recentConvs = await db.Conversations
            .Include(c => c.ActiveAgent)
            .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1))
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.LastActivityAt)
            .Take(10)
            .ToListAsync(ct);

        var recentConversations = recentConvs.Select(c => new
        {
            id              = c.Id,
            clientPhone     = c.ClientPhone,
            clientName      = c.ClientName,
            agentType       = c.ActiveAgent?.Type.ToString() ?? "General",
            status          = c.Status.ToString(),
            isHumanHandled  = c.IsHumanHandled,
            lastActivityAt  = c.LastActivityAt,
            lastMessagePreview = c.Messages
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault()?.Content[..Math.Min(80,
                    c.Messages.OrderByDescending(m => m.SentAt)
                        .FirstOrDefault()?.Content.Length ?? 0)]
        });

        return Ok(new
        {
            totalConversations,
            activeAgents,
            activeCampaigns,
            escalatedCount,
            gestionByResult = gestionByResult.ToDictionary(x => x.Result, x => x.Count),
            recentConversations,
        });
    }
}
