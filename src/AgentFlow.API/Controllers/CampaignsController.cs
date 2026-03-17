using AgentFlow.Application.Modules.Campaigns;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

[ApiController]
// [Authorize] // TODO: habilitar cuando auth esté configurado
[Route("api/campaigns")]
public class CampaignsController(IMediator mediator, ITenantContext tenantCtx) : ControllerBase
{
    /// <summary>
    /// Sube un archivo de contactos e inicia la campaña.
    /// El File Processor valida teléfonos antes de disparar mensajes.
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadAndStart(
        [FromForm] CampaignUploadRequest req,
        IFormFile file,
        CancellationToken ct)
    {
        // TODO: parsear CSV/Excel, validar teléfonos, crear ContactRows
        var contacts = new List<ContactRow>();
        var campaignId = await mediator.Send(new StartCampaignCommand(
            tenantCtx.TenantId,
            req.Name,
            req.AgentId,
            ChannelType.WhatsApp,
            CampaignTrigger.FileUpload,
            contacts,
            User.Identity!.Name!,
            req.ScheduledAt
        ), ct);

        return Ok(new { campaignId });
    }
}

public record CampaignUploadRequest(
    string Name,
    Guid AgentId,
    DateTime? ScheduledAt
);
