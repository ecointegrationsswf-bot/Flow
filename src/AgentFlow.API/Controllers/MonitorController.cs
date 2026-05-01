using AgentFlow.Application.Modules.Monitor;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
[Authorize]
[Route("api/monitor")]
public class MonitorController(IMediator mediator, ITenantContext tenantCtx, AgentFlowDbContext db) : ControllerBase
{
    /// <summary>
    /// Lista usuarios que han lanzado campañas en este tenant — para poblar el
    /// selector de filtro en el Monitor. Devuelve identificador (lo que se guardó
    /// en Campaign.LaunchedByUserId — puede ser email/fullName/guid) y un display.
    /// </summary>
    [HttpGet("campaign-launchers")]
    public async Task<IActionResult> GetCampaignLaunchers(CancellationToken ct)
    {
        var keys = await db.Campaigns
            .Where(c => c.TenantId == tenantCtx.TenantId
                     && c.LaunchedByUserId != null
                     && c.LaunchedByUserId != "")
            .Select(c => c.LaunchedByUserId!)
            .Distinct()
            .ToListAsync(ct);

        // Resolver nombres legibles desde AppUsers/SuperAdmins (la clave puede ser id, email o fullName).
        var users = await db.AppUsers
            .Where(u => keys.Contains(u.Id.ToString())
                     || keys.Contains(u.Email)
                     || keys.Contains(u.FullName))
            .Select(u => new { Id = u.Id.ToString(), u.Email, u.FullName })
            .ToListAsync(ct);

        var sas = await db.SuperAdmins
            .Where(s => keys.Contains(s.Id.ToString())
                     || keys.Contains(s.Email)
                     || keys.Contains(s.FullName))
            .Select(s => new { Id = s.Id.ToString(), s.Email, s.FullName })
            .ToListAsync(ct);

        var resolved = keys.Select(k =>
        {
            var u = users.FirstOrDefault(x => x.Id == k || x.Email == k || x.FullName == k)
                 ?? sas.FirstOrDefault(x => x.Id == k || x.Email == k || x.FullName == k);
            var label = u?.FullName ?? u?.Email ?? k;
            return new { key = k, label };
        })
        .OrderBy(x => x.label)
        .ToList();

        return Ok(resolved);
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetActive(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? launchedByUserId,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetActiveConversationsQuery(
            tenantCtx.TenantId,
            from?.ToUniversalTime(),
            to?.ToUniversalTime(),
            launchedByUserId), ct);
        return Ok(result);
    }

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetConversationDetailQuery(tenantCtx.TenantId, id), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Ejecutivo toma la conversación — pausa el agente IA manualmente.
    /// </summary>
    [HttpPost("conversations/{id:guid}/take")]
    public async Task<IActionResult> TakeConversation(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var result = await mediator.Send(new PauseAgentCommand(tenantCtx.TenantId, id, userId), ct);
        if (!result.Success) return NotFound(new { error = result.Message });

        var detail = await mediator.Send(new GetConversationDetailQuery(tenantCtx.TenantId, id), ct);
        return Ok(new { success = true, conversation = detail });
    }

    /// <summary>
    /// Reactiva el agente IA — devuelve el control al agente.
    /// </summary>
    [HttpPost("conversations/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateAgent(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new ReactivateAgentCommand(tenantCtx.TenantId, id), ct);
        if (!result.Success) return NotFound(new { error = result.Message });

        var detail = await mediator.Send(new GetConversationDetailQuery(tenantCtx.TenantId, id), ct);
        return Ok(new { success = true, conversation = detail });
    }

    /// <summary>
    /// Ejecutivo envía mensaje directamente al cliente desde el monitor.
    /// Guarda en BD y envía por WhatsApp.
    /// </summary>
    [HttpPost("conversations/{id:guid}/reply")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto?.Message))
            return BadRequest(new { error = "El mensaje no puede estar vacío" });

        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var result = await mediator.Send(
                new HumanReplyCommand(tenantCtx.TenantId, id, dto.Message, userId), ct);

            Console.WriteLine($"[Reply] Guardado messageId={result.MessageId} sent={result.WasSentToWhatsApp}");

            // Retornar la conversación completa actualizada para que el frontend la use directamente
            var detail = await mediator.Send(new GetConversationDetailQuery(tenantCtx.TenantId, id), ct);

            return Ok(new {
                messageId   = result.MessageId,
                sent        = result.WasSentToWhatsApp,
                conversation = detail
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Reply] Error: {ex.Message}\n{ex.StackTrace}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    /// <summary>
    /// Ejecutivo envía una imagen o documento al cliente desde el monitor.
    /// Sube el archivo a Azure Blob y lo envía por WhatsApp.
    /// </summary>
    [HttpPost("conversations/{id:guid}/file")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> SendFile(
        Guid id,
        IFormFile file,
        [FromServices] IBlobStorageService blobStorage,
        [FromServices] IConversationRepository conversations,
        [FromServices] IChannelProviderFactory channelFactory,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo requerido." });

        var conv = await conversations.GetByIdAsync(id, ct);
        if (conv is null || conv.TenantId != tenantCtx.TenantId)
            return NotFound();

        // ── 1. Subir a Azure Blob ────────────────────────
        string azureUrl;
        try
        {
            var ext      = Path.GetExtension(file.FileName).TrimStart('.').ToLower();
            var blobName = $"conversations/{conv.TenantId}/{DateTime.UtcNow:yyyyMMdd}/{Guid.NewGuid()}.{ext}";
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            azureUrl = await blobStorage.UploadWhatsAppMediaAsync(blobName, ms.ToArray(), file.ContentType, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SendFile] Error Azure: {ex.Message}");
            return StatusCode(500, new { error = "No se pudo subir el archivo." });
        }

        // ── 2. Determinar tipo de media ──────────────────
        var isImage    = file.ContentType.StartsWith("image/");
        var mediaType  = isImage ? "image" : "document";
        var msgContent = isImage
            ? $"📷 [Imagen enviada]\n[media:{azureUrl}]"
            : $"📄 [{file.FileName}]\n[media:{azureUrl}]";

        // ── 3. Guardar mensaje en BD ─────────────────────
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var message = new Message
        {
            Id             = Guid.NewGuid(),
            ConversationId = conv.Id,
            Direction      = MessageDirection.Outbound,
            Status         = MessageStatus.Sent,
            Content        = msgContent,
            IsFromAgent    = false,
            AgentName      = "Ejecutivo",
            SentAt         = DateTime.UtcNow
        };
        await conversations.AddMessageAsync(message, ct);

        // ── 4. Enviar por WhatsApp ───────────────────────
        try
        {
            var provider = await channelFactory.GetProviderAsync(conv.TenantId, ct);
            if (provider is not null)
            {
                var sendReq = new SendMessageRequest(
                    conv.ClientPhone, "", azureUrl, mediaType, file.FileName);
                var result = await provider.SendMessageAsync(sendReq, ct);
                message.ExternalMessageId = result.ExternalMessageId;
                if (!result.Success) message.Status = MessageStatus.Failed;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SendFile] Error WhatsApp: {ex.Message}");
            message.Status = MessageStatus.Failed;
        }

        // ── 5. Pausar IA y guardar ───────────────────────
        conv.IsHumanHandled  = true;
        conv.HandledByUserId = userId;
        conv.Status          = ConversationStatus.EscalatedToHuman;
        conv.LastActivityAt  = DateTime.UtcNow;
        await conversations.SaveChangesAsync(ct);

        var detail = await mediator.Send(new GetConversationDetailQuery(tenantCtx.TenantId, id), ct);
        return Ok(new { conversation = detail });
    }
}

public class ReplyDto
{
    public string Message { get; set; } = string.Empty;
}
