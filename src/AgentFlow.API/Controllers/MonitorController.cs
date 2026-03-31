using AgentFlow.Application.Modules.Monitor;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Storage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

[ApiController]
[Authorize]
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
