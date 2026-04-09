using AgentFlow.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Punto de entrada HTTP del Cerebro.
/// N8n llama a POST /api/brain/route para cada mensaje entrante.
/// </summary>
[ApiController]
[Route("api/brain")]
public class BrainController(IBrainService brain) : ControllerBase
{
    [HttpPost("route")]
    public async Task<IActionResult> Route([FromBody] BrainRequest request, CancellationToken ct)
    {
        if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.ContactId))
            return BadRequest(new { error = "TenantId y ContactId son obligatorios." });

        var decision = await brain.RouteAsync(request, ct);

        return Ok(new
        {
            decision.AgentSlug,
            decision.Intent,
            sessionState = decision.SessionState.ToString(),
            decision.ValidationPending,
            decision.MessageToClient
        });
    }
}
