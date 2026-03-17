using AgentFlow.Application.Modules.Monitor;
using AgentFlow.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

[ApiController]
// [Authorize] // TODO: habilitar cuando auth esté configurado
[Route("api/monitor")]
public class MonitorController(IMediator mediator, ITenantContext tenantCtx) : ControllerBase
{
    [HttpGet("conversations")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var result = await mediator.Send(new GetActiveConversationsQuery(tenantCtx.TenantId), ct);
        return Ok(result);
    }

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        // TODO: GetConversationDetailQuery
        return Ok(new { id });
    }

    /// <summary>
    /// Ejecutivo toma la conversación — pausa el agente IA.
    /// </summary>
    [HttpPost("conversations/{id:guid}/take")]
    public async Task<IActionResult> TakeConversation(Guid id, CancellationToken ct)
    {
        // TODO: TakeConversationCommand — pausa en Redis + notifica SignalR
        return Ok(new { id, takenBy = User.Identity?.Name });
    }

    /// <summary>
    /// Ejecutivo envía mensaje directamente en la conversación.
    /// </summary>
    [HttpPost("conversations/{id:guid}/reply")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyDto dto, CancellationToken ct)
    {
        // TODO: HumanReplyCommand
        return Ok(new { id, dto.Message });
    }
}

public record ReplyDto(string Message);
