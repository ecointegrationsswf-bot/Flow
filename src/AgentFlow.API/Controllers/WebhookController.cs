using AgentFlow.Application.Modules.Webhooks;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController(IMediator mediator, ITenantContext tenantCtx) : ControllerBase
{
    /// <summary>
    /// Endpoint universal para UltraMsg y Meta Cloud API.
    /// n8n también puede hacer POST aquí tras normalizar el payload.
    /// </summary>
    [HttpPost("message")]
    public async Task<IActionResult> IncomingMessage(
        [FromBody] IncomingMessageDto dto, CancellationToken ct)
    {
        var result = await mediator.Send(new ProcessIncomingMessageCommand(
            tenantCtx.TenantId,
            dto.From,
            dto.Body,
            Enum.Parse<ChannelType>(dto.Channel, true),
            dto.ClientName,
            dto.ExternalId
        ), ct);

        return Ok(new { result.ConversationId, result.ReplyText, result.AgentType });
    }

    /// <summary>
    /// Verificación de webhook de Meta Cloud API (GET challenge).
    /// </summary>
    [HttpGet("meta/verify")]
    public IActionResult MetaVerify(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.challenge")] string challenge,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromServices] IConfiguration config)
    {
        var expected = config["Meta:VerifyToken"];
        if (mode == "subscribe" && token == expected)
            return Ok(int.Parse(challenge));
        return Forbid();
    }
}

public record IncomingMessageDto(
    string From,
    string Body,
    string Channel,
    string? ClientName,
    string? ExternalId
);
