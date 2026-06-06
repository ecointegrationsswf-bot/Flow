using AgentFlow.Application.Modules.Campaigns;
using AgentFlow.Application.Modules.Campaigns.LaunchV2;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record CampaignUploadRequest(string Name, Guid AgentId, DateTime? ScheduledAt, string? Channel = null);
public record LaunchV2Request(int WarmupDay = 0);
public record UploadFixedFormatRequest([Required] string Name, [Required] Guid AgentId, DateTime? ScheduledAt, Guid? CampaignTemplateId = null, string? Channel = null);

public record CreateCampaignFromFileRequest(
    string Name,
    Guid AgentId,
    DateTime? ScheduledAt,
    string TempFilePath,
    Dictionary<string, string> ColumnMapping,
    // Canal de envío. Si no se especifica, se auto-detecta del agente:
    //   - Si el agente tiene 1 canal habilitado → se usa ese
    //   - Si tiene varios → debe venir explícito en la request (sino 400)
    string? Channel = null
);

[ApiController]
[Authorize]
[Route("api/campaigns")]
public class CampaignsController(
    IMediator mediator,
    ITenantContext tenantCtx,
    IExcelFileProcessor excelProcessor,
    IFixedFormatCampaignService fixedFormatService,
    IBlobStorageService blobStorage,
    ICampaignRepository campaignRepo,
    AgentFlowDbContext db,
    IConfiguration cfg,
    IHttpClientFactory httpClientFactory,
    IUltraMsgInstanceService ultraMsg,
    AgentFlow.Infrastructure.Channels.MetaCloudApi.IMetaCloudApiHealthService metaHealth) : ControllerBase
{
    // Devuelve el nombre completo del usuario autenticado (claim full_name),
    // con fallback a email y luego a "system".
    private string CurrentUser =>
        User.FindFirst("full_name")?.Value
        ?? User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? User.Identity?.Name
        ?? "system";

    /// <summary>
    /// Lista todas las campañas del tenant autenticado.
    /// Muestra: nombre, estado, total contactos, fecha creación, etc.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var campaigns = await campaignRepo.ListByTenantAsync(tenantCtx.TenantId, ct);
        return Ok(campaigns.Select(c => new
        {
            c.Id,
            c.Name,
            Channel = c.Channel.ToString(),
            Trigger = c.Trigger.ToString(),
            c.IsActive,
            c.TotalContacts,
            c.ProcessedContacts,
            c.ScheduledAt,
            c.StartedAt,
            c.CompletedAt,
            c.CreatedAt,
            c.SourceFileName,
            Status = c.Status.ToString(),
            c.LaunchedAt,
            c.CreatedByUserId,
            c.LaunchedByUserId,
            // Progreso en porcentaje
            Progress = c.TotalContacts > 0
                ? Math.Round((double)c.ProcessedContacts / c.TotalContacts * 100, 1)
                : 0
        }));
    }

    /// <summary>
    /// Obtiene el detalle de una campaña con todos sus contactos.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var campaign = await campaignRepo.GetByIdAsync(id, tenantCtx.TenantId, ct);
        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });

        return Ok(new
        {
            campaign.Id,
            campaign.Name,
            Channel = campaign.Channel.ToString(),
            Trigger = campaign.Trigger.ToString(),
            campaign.IsActive,
            campaign.TotalContacts,
            campaign.ProcessedContacts,
            campaign.ScheduledAt,
            campaign.StartedAt,
            campaign.CompletedAt,
            campaign.CreatedAt,
            campaign.SourceFileName,
            Contacts = campaign.Contacts.Select(cc => new
            {
                cc.Id,
                cc.PhoneNumber,
                cc.ClientName,
                cc.PolicyNumber,
                cc.InsuranceCompany,
                cc.PendingAmount,
                cc.IsPhoneValid,
                cc.RetryCount,
                Result = cc.Result.ToString(),
                cc.LastContactAt
            })
        });
    }

    /// <summary>
    /// Lanza la campaña usando el flujo v2 (en proceso, sin n8n). Aplica
    /// dedup contra campañas activas del tenant y warm-up por día. El envío
    /// real lo realiza el CampaignWorker en el Worker Service.
    /// </summary>
    [HttpPost("{id:guid}/launch-v2")]
    public async Task<IActionResult> LaunchV2(Guid id, [FromBody] LaunchV2Request? body, CancellationToken ct)
    {
        var lineCheck = await ValidateWhatsAppLineForLaunchAsync(id, tenantCtx.TenantId, ct);
        if (lineCheck is not null)
            return BadRequest(new { error = lineCheck });

        var phone = User.FindFirst("phone")?.Value;
        var result = await mediator.Send(new LaunchCampaignV2Command(
            CampaignId: id,
            TenantId: tenantCtx.TenantId,
            LaunchedByUserId: CurrentUser,
            LaunchedByUserPhone: phone,
            WarmupDay: body?.WarmupDay ?? 0
        ), ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Lanza el envío de una campaña. Internamente delega al flujo v2 (CampaignWorker
    /// en el Worker Service) — el endpoint queda como alias por compatibilidad con
    /// clientes existentes. El parámetro <c>warmupDay</c> es opcional (default 0).
    /// </summary>
    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> StartCampaign(
        Guid id,
        [FromQuery] int warmupDay = 0,
        CancellationToken ct = default)
    {
        var lineCheck = await ValidateWhatsAppLineForLaunchAsync(id, tenantCtx.TenantId, ct);
        if (lineCheck is not null)
            return BadRequest(new { error = lineCheck });

        var phone = User.FindFirst("phone")?.Value;
        var result = await mediator.Send(new LaunchCampaignV2Command(
            CampaignId: id,
            TenantId: tenantCtx.TenantId,
            LaunchedByUserId: CurrentUser,
            LaunchedByUserPhone: phone,
            WarmupDay: warmupDay
        ), ct);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Validación previa al lanzamiento para campañas de canal WhatsApp:
    /// el agente DEBE tener una WhatsAppLine vinculada, esa línea debe estar
    /// IsActive=true y el ping en vivo a UltraMsg debe devolver "authenticated".
    /// Si todo OK devuelve null. Si no, devuelve el mensaje para mostrar al usuario
    /// en el modal de validaciones del frontend.
    ///
    /// El ping en vivo es importante: el job diario WHATSAPP_LINE_HEALTH_CHECK
    /// corre solo a las 6am, así que <c>LastStatus</c> cacheado puede estar
    /// desactualizado hasta 24h. Para una operación crítica como lanzar campaña
    /// vale 1-2 segundos extra para validar el estado real.
    ///
    /// De paso refrescamos <c>LastStatus</c>/<c>LastStatusCheckedAt</c> con el
    /// resultado del ping — beneficia a la próxima ejecución del job diario.
    /// </summary>
    private async Task<string?> ValidateWhatsAppLineForLaunchAsync(
        Guid campaignId, Guid tenantId, CancellationToken ct)
    {
        var campaign = await db.Campaigns
            .Where(c => c.Id == campaignId && c.TenantId == tenantId)
            .Select(c => new { c.Channel, c.AgentDefinitionId, c.CampaignTemplateId })
            .FirstOrDefaultAsync(ct);

        if (campaign is null) return null; // El handler maneja el 404
        if (campaign.Channel != ChannelType.WhatsApp) return null; // Email/SMS no requieren línea

        var agent = await db.AgentDefinitions
            .Where(a => a.Id == campaign.AgentDefinitionId)
            .Select(a => new { a.Name, a.AvatarName, a.WhatsAppLineId })
            .FirstOrDefaultAsync(ct);

        var agentLabel = agent?.AvatarName ?? agent?.Name ?? "el agente";

        if (agent is null || !agent.WhatsAppLineId.HasValue)
            return $"No se puede lanzar la campaña: {agentLabel} no tiene una línea de WhatsApp asociada. Vincula una línea desde Agentes IA → Editar agente.";

        var line = await db.Set<Domain.Entities.WhatsAppLine>()
            .FirstOrDefaultAsync(l => l.Id == agent.WhatsAppLineId.Value, ct);

        if (line is null)
            return $"La línea de WhatsApp asociada a {agentLabel} no existe. Revisa la configuración del agente.";

        if (!line.IsActive)
            return $"La línea de WhatsApp \"{line.DisplayName}\" está deshabilitada. Actívala en Configuración → WhatsApp antes de lanzar la campaña.";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // ── Línea Meta: salud vía Graph API + exigir plantilla aprobada del maestro ──
        if (line.Provider == ProviderType.MetaCloudApi)
        {
            string metaStatus;
            try
            {
                var h = await metaHealth.GetHealthAsync(line.InstanceId, line.MetaAccessToken ?? "", null, cts.Token);
                metaStatus = h.Status;
            }
            catch
            {
                return $"No fue posible verificar el estado de la línea Meta \"{line.DisplayName}\". Intenta de nuevo en unos segundos.";
            }

            line.LastStatus = metaStatus;
            line.LastStatusCheckedAt = DateTime.UtcNow;
            if (string.Equals(metaStatus, "authenticated", StringComparison.OrdinalIgnoreCase))
                line.ConsecutivePingFailures = 0;
            await db.SaveChangesAsync(ct);

            if (!string.Equals(metaStatus, "authenticated", StringComparison.OrdinalIgnoreCase))
                return $"La línea Meta \"{line.DisplayName}\" no está operativa (estado: {metaStatus}). Revisá el token/WABA en Configuración → WhatsApp.";

            // Regla Fase 2: Meta exige plantilla aprobada para iniciar en frío. El maestro
            // debe tener al menos UNA plantilla aprobada y activa.
            var approvedTemplates = campaign.CampaignTemplateId.HasValue
                ? await db.Set<Domain.Entities.MetaMessageTemplate>().CountAsync(t =>
                    t.TenantId == tenantId
                    && t.CampaignTemplateId == campaign.CampaignTemplateId
                    && t.Purpose == Domain.Entities.MetaTemplatePurposes.Launch
                    && t.MetaStatus == Domain.Entities.MetaTemplateStatuses.Approved
                    && t.IsEnabled, ct)
                : 0;

            if (approvedTemplates == 0)
                return "No se puede lanzar la campaña: el maestro no tiene plantillas de Meta de tipo Lanzamiento aprobadas y activas. " +
                       "Creá o aprobá al menos una en el maestro → pestaña \"Plantillas Meta\" (y esperá la aprobación de Meta) antes de lanzar.";

            return null;
        }

        // ── Línea UltraMsg: ping en vivo /instance/status ──
        string status;
        try
        {
            var result = await ultraMsg.GetStatusAsync(line.InstanceId, line.ApiToken, cts.Token);
            status = string.IsNullOrWhiteSpace(result.Status) ? "unknown" : result.Status.ToLowerInvariant();
        }
        catch
        {
            // Timeout o error de red — bloqueamos y no marcamos failure persistente
            // (un flake puntual no debe contaminar el contador del job diario).
            return $"No fue posible verificar el estado de la línea \"{line.DisplayName}\" en UltraMsg. Intenta de nuevo en unos segundos o reconecta desde Configuración → WhatsApp.";
        }

        // Refresca el cache para el job diario.
        line.LastStatus = status;
        line.LastStatusCheckedAt = DateTime.UtcNow;
        if (string.Equals(status, "authenticated", StringComparison.OrdinalIgnoreCase))
            line.ConsecutivePingFailures = 0;
        await db.SaveChangesAsync(ct);

        if (!string.Equals(status, "authenticated", StringComparison.OrdinalIgnoreCase))
            return $"La línea \"{line.DisplayName}\" no está conectada (estado: {status}). Reconéctala desde Configuración → WhatsApp antes de lanzar la campaña.";

        return null;
    }

    /// <summary>
    /// Pausa una campaña activa (detiene el envío de mensajes).
    /// </summary>
    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> PauseCampaign(Guid id, CancellationToken ct)
    {
        var campaign = await campaignRepo.GetByIdAsync(id, tenantCtx.TenantId, ct);
        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });

        campaign.IsActive = false;
        await campaignRepo.UpdateAsync(campaign, ct);

        return Ok(new { message = "Campaña pausada. Los envíos pendientes se detienen." });
    }

    /// <summary>
    /// Reactiva una campaña pausada. Solo marca <c>IsActive=true</c>; el CampaignWorker
    /// la recoge en el próximo tick (≤30s) si está en estado Running.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> ResumeCampaign(Guid id, CancellationToken ct)
    {
        var campaign = await campaignRepo.GetByIdAsync(id, tenantCtx.TenantId, ct);
        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });

        campaign.IsActive = true;
        await campaignRepo.UpdateAsync(campaign, ct);

        return Ok(new { message = "Campaña reactivada. El Worker la recogerá en el próximo tick." });
    }

    /// <summary>
    /// Cancela una campaña de forma IRREVERSIBLE. A diferencia de Pause/Resume,
    /// Cancel marca la campaña como terminada definitivamente y descarta todos
    /// los contactos pendientes — el operativo no la podrá reanudar después.
    ///
    /// Comportamiento por estado del contacto:
    /// - Pending/Queued/Deferred/Retry → Skipped (con DispatchError = "Campaña cancelada")
    /// - Claimed → se respeta el envío en curso (no se interrumpe HTTP a UltraMsg)
    /// - Sent/Skipped/Duplicate/Error → no se tocan
    ///
    /// NO cierra conversaciones abiertas: los clientes que ya recibieron el
    /// mensaje siguen siendo gestionados por el agente conversacional.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelCampaign(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var campaign = await campaignRepo.GetByIdAsync(id, tenantId, ct);
        if (campaign is null)
            return NotFound(new { error = "Campaña no encontrada." });

        if (campaign.Status is CampaignStatus.Completed
                            or CampaignStatus.Failed
                            or CampaignStatus.Cancelled)
        {
            return BadRequest(new
            {
                error = $"No se puede cancelar una campaña en estado {campaign.Status}.",
            });
        }

        // Marcar como Skipped los contactos pendientes (Pending, Queued, Deferred, Retry).
        // Claimed NO se toca: hay un envío HTTP en curso y abortarlo a mitad
        // genera inconsistencia. Cuando termine quedará Sent o Error y ya no
        // procesará nada más porque IsActive=false.
        var skippedCount = await db.CampaignContacts
            .Where(cc => cc.CampaignId == id
                      && (cc.DispatchStatus == DispatchStatus.Pending
                       || cc.DispatchStatus == DispatchStatus.Queued
                       || cc.DispatchStatus == DispatchStatus.Deferred
                       || cc.DispatchStatus == DispatchStatus.Retry))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(cc => cc.DispatchStatus, DispatchStatus.Skipped)
                .SetProperty(cc => cc.DispatchError, "Campaña cancelada"),
                ct);

        campaign.Status = CampaignStatus.Cancelled;
        campaign.IsActive = false;
        if (campaign.CompletedAt is null)
            campaign.CompletedAt = DateTime.UtcNow;
        await campaignRepo.UpdateAsync(campaign, ct);

        return Ok(new
        {
            message = $"Campaña cancelada. {skippedCount} contactos pendientes marcados como descartados.",
            skippedCount,
        });
    }

    /// <summary>
    /// Lista paginada de contactos de una campaña con su estado de despacho.
    /// Usado por la pantalla de detalle de contactos en el portal del tenant.
    ///
    /// Filtros:
    /// - status: All | Sent | Pending | Failed | Discarded
    ///   * Sent      → DispatchStatus = Sent
    ///   * Pending   → IN (Pending, Queued, Claimed, Deferred, Retry)
    ///   * Failed    → DispatchStatus = Error
    ///   * Discarded → IN (Skipped, Duplicate)
    /// - q: texto libre, busca en ClientName y PhoneNumber.
    /// </summary>
    [HttpGet("{id:guid}/contacts")]
    public async Task<IActionResult> ListContacts(
        Guid id,
        [FromQuery] string? status = "All",
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = tenantCtx.TenantId;
        var campaignExists = await db.Campaigns
            .AnyAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (!campaignExists)
            return NotFound(new { error = "Campaña no encontrada." });

        var query = BuildContactsQuery(id, tenantId, status, q);
        var total = await query.CountAsync(ct);

        // Orden: enviados primero por SentAt desc, luego pendientes/errores por CreatedAt
        // — útil para que el operativo vea los más recientes arriba.
        var items = await query
            .OrderByDescending(cc => cc.SentAt ?? cc.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(Math.Clamp(pageSize, 1, 500))
            .Select(cc => new
            {
                cc.Id,
                cc.PhoneNumber,
                cc.ClientName,
                cc.PolicyNumber,
                cc.InsuranceCompany,
                cc.PendingAmount,
                cc.GeneratedMessage,
                DispatchStatus = cc.DispatchStatus.ToString(),
                cc.SentAt,
                cc.ExternalMessageId,
                cc.DispatchError,
                cc.IsPhoneValid,
            })
            .ToListAsync(ct);

        // Contadores por bucket para el contador de las tabs del frontend
        var byBucket = await db.CampaignContacts
            .Where(cc => cc.CampaignId == id)
            .GroupBy(cc => cc.DispatchStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var sent      = byBucket.Where(b => b.Status == DispatchStatus.Sent).Sum(b => b.Count);
        var pending   = byBucket.Where(b => b.Status == DispatchStatus.Pending
                                         || b.Status == DispatchStatus.Queued
                                         || b.Status == DispatchStatus.Claimed
                                         || b.Status == DispatchStatus.Deferred
                                         || b.Status == DispatchStatus.Retry).Sum(b => b.Count);
        var failed    = byBucket.Where(b => b.Status == DispatchStatus.Error).Sum(b => b.Count);
        var discarded = byBucket.Where(b => b.Status == DispatchStatus.Skipped
                                         || b.Status == DispatchStatus.Duplicate).Sum(b => b.Count);

        return Ok(new
        {
            total,
            page,
            pageSize,
            items,
            counts = new { all = sent + pending + failed + discarded, sent, pending, failed, discarded }
        });
    }

    /// <summary>
    /// Devuelve los mensajes asociados a un contacto de campaña — útil para el
    /// modal "ojo" de la lista, donde queremos mostrar el mensaje inicial, los
    /// emails enviados, y eventualmente toda la conversación con el cliente.
    ///
    /// La asociación es Campaign.Id + ClientPhone == contact.PhoneNumber.
    /// Devolvemos todos los Messages de la Conversation matcheada,
    /// independientemente del canal (WhatsApp, Email, SMS).
    /// </summary>
    [HttpGet("{id:guid}/contacts/{contactId:guid}/messages")]
    public async Task<IActionResult> GetContactMessages(
        Guid id, Guid contactId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var contact = await db.CampaignContacts
            .Where(cc => cc.Id == contactId && cc.CampaignId == id)
            .Select(cc => new { cc.PhoneNumber })
            .FirstOrDefaultAsync(ct);
        if (contact is null) return NotFound(new { error = "Contacto no encontrado." });

        // Una conversación por (TenantId, CampaignId, ClientPhone) — la que abrió
        // el bot al ejecutar la campaña. Si hay más de una (raro), tomamos la
        // más reciente.
        var conversation = await db.Conversations
            .Where(c => c.TenantId == tenantId && c.CampaignId == id && c.ClientPhone == contact.PhoneNumber)
            .OrderByDescending(c => c.LastActivityAt)
            .Select(c => new { c.Id, c.Channel })
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
        {
            // No hay conversación todavía (la campaña aún no lanzó el primer mensaje).
            return Ok(new { conversationId = (Guid?)null, items = Array.Empty<object>() });
        }

        var messages = await db.Messages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.SentAt)
            .Select(m => new
            {
                m.Id,
                m.Content,
                m.IsFromAgent,
                Direction = m.Direction.ToString(),
                m.SentAt,
                m.ExternalMessageId,
                m.AgentName,
                m.DetectedIntent,
                Channel = m.Channel.HasValue ? m.Channel.Value.ToString() : null,
                m.Subject,
                m.Recipient,
                Status = m.Status.ToString(),
            })
            .ToListAsync(ct);

        return Ok(new { conversationId = (Guid?)conversation.Id, items = messages });
    }

    /// <summary>
    /// Exporta el listado filtrado de contactos a Excel (.xlsx). Mismo
    /// criterio de filtro que ListContacts pero SIN paginar — exporta el
    /// universo completo del filtro actual.
    /// </summary>
    [HttpGet("{id:guid}/contacts/export")]
    public async Task<IActionResult> ExportContacts(
        Guid id,
        [FromQuery] string? status = "All",
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        var tenantId = tenantCtx.TenantId;
        var campaign = await db.Campaigns
            .Where(c => c.Id == id && c.TenantId == tenantId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(ct);
        if (campaign is null)
            return NotFound(new { error = "Campaña no encontrada." });

        var rows = await BuildContactsQuery(id, tenantId, status, q)
            .OrderByDescending(cc => cc.SentAt ?? cc.CreatedAt)
            .Select(cc => new
            {
                cc.PhoneNumber,
                cc.ClientName,
                cc.GeneratedMessage,
                Status = cc.DispatchStatus.ToString(),
                cc.SentAt,
                cc.ExternalMessageId,
                cc.DispatchError,
            })
            .ToListAsync(ct);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Contactos");
        ws.Cell(1, 1).Value = "Cliente";
        ws.Cell(1, 2).Value = "Teléfono";
        ws.Cell(1, 3).Value = "Mensaje enviado";
        ws.Cell(1, 4).Value = "Estado";
        ws.Cell(1, 5).Value = "Enviado";
        ws.Cell(1, 6).Value = "ID mensaje externo";
        ws.Cell(1, 7).Value = "Error";
        ws.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = r.ClientName ?? "";
            ws.Cell(row, 2).Value = r.PhoneNumber;
            ws.Cell(row, 3).Value = r.GeneratedMessage ?? "";
            ws.Cell(row, 3).Style.Alignment.WrapText = true;
            ws.Cell(row, 3).Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Top;
            ws.Cell(row, 4).Value = r.Status;
            // SentAt viene como UTC; convertimos a hora Panamá para Excel.
            if (r.SentAt.HasValue)
            {
                ws.Cell(row, 5).Value = r.SentAt.Value.AddHours(-5);
                ws.Cell(row, 5).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            }
            ws.Cell(row, 6).Value = r.ExternalMessageId ?? "";
            ws.Cell(row, 7).Value = r.DispatchError ?? "";
        }

        // AdjustToContents respeta wrap text de la columna 3; las otras se ajustan al texto.
        ws.Columns(1, 2).AdjustToContents();
        ws.Column(3).Width = 80;   // Mensaje — ancho fijo cómodo para lectura
        ws.Columns(4, 7).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();

        var safeName = string.Join("_", campaign.Name.Split(Path.GetInvalidFileNameChars()));
        var fileName = $"contactos_{safeName}_{DateTime.UtcNow.AddHours(-5):yyyyMMdd_HHmm}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private IQueryable<Domain.Entities.CampaignContact> BuildContactsQuery(
        Guid campaignId, Guid tenantId, string? status, string? q)
    {
        var query = db.CampaignContacts
            .Where(cc => cc.CampaignId == campaignId
                      && cc.Campaign.TenantId == tenantId);

        switch ((status ?? "All").Trim().ToLowerInvariant())
        {
            case "sent":
                query = query.Where(cc => cc.DispatchStatus == DispatchStatus.Sent);
                break;
            case "pending":
                query = query.Where(cc =>
                    cc.DispatchStatus == DispatchStatus.Pending
                 || cc.DispatchStatus == DispatchStatus.Queued
                 || cc.DispatchStatus == DispatchStatus.Claimed
                 || cc.DispatchStatus == DispatchStatus.Deferred
                 || cc.DispatchStatus == DispatchStatus.Retry);
                break;
            case "failed":
                query = query.Where(cc => cc.DispatchStatus == DispatchStatus.Error);
                break;
            case "discarded":
                query = query.Where(cc =>
                    cc.DispatchStatus == DispatchStatus.Skipped
                 || cc.DispatchStatus == DispatchStatus.Duplicate);
                break;
            // "all" o cualquier otro → sin filtro
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var qLower = q.Trim().ToLower();
            query = query.Where(cc =>
                (cc.ClientName != null && cc.ClientName.ToLower().Contains(qLower)) ||
                cc.PhoneNumber.ToLower().Contains(qLower));
        }

        return query;
    }

    [HttpPost("parse")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ParseFile(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "Archivo vacio." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "El archivo excede el limite de 10 MB." });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext is not ".xlsx" and not ".xls" and not ".csv")
            return BadRequest(new { error = "Formato no soportado. Use Excel (.xlsx) o CSV." });

        var allowedMimeTypes = new[] {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel", "text/csv", "application/octet-stream"
        };
        if (!allowedMimeTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new { error = "Tipo de archivo no permitido." });

        using var stream = file.OpenReadStream();
        var result = excelProcessor.ParseExcel(stream, file.FileName);

        if (result.DetectedColumns.Count == 0)
            return BadRequest(new { error = "No se detectaron columnas en el archivo." });

        // Guardar archivo temporal en blob para paso 2
        var tempPath = $"temp/{tenantCtx.TenantId}/{Guid.NewGuid()}{ext}";
        using var uploadStream = file.OpenReadStream();
        await blobStorage.UploadAsync(tempPath, uploadStream, file.ContentType, ct);

        return Ok(new
        {
            result.DetectedColumns,
            result.PreviewRows,
            result.TotalRows,
            TempFilePath = tempPath
        });
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateFromFile([FromBody] CreateCampaignFromFileRequest req, CancellationToken ct)
    {
        // Resolver/validar el canal contra los canales habilitados del agente.
        // Si el agente no tiene canales → rechazo total.
        // Si tiene 1 → se usa ese, ignoramos lo que venga en el request.
        // Si tiene >1 → el request DEBE especificar Channel y debe estar entre los habilitados.
        var channelResolution = await ResolveCampaignChannelAsync(req.AgentId, req.Channel, ct);
        if (channelResolution.Error is not null) return channelResolution.Error;
        var resolvedChannel = channelResolution.Channel;

        var (fileStream, _) = await blobStorage.DownloadAsync(req.TempFilePath, ct);
        List<ContactRow> contactRows;
        using (fileStream)
        {
            contactRows = ParseWithMapping(fileStream, req.ColumnMapping);
        }

        if (contactRows.Count == 0)
            return BadRequest(new { error = "No se encontraron contactos validos." });

        var campaignId = await mediator.Send(new StartCampaignCommand(
            tenantCtx.TenantId,
            req.Name,
            req.AgentId,
            resolvedChannel,
            CampaignTrigger.FileUpload,
            contactRows,
            CurrentUser,
            req.ScheduledAt
        ), ct);

        // Limpiar archivo temporal
        try { await blobStorage.DeleteAsync(req.TempFilePath, ct); } catch { }

        return Ok(new { campaignId, contactCount = contactRows.Count });
    }

    /// <summary>
    /// Resuelve el canal a usar para una campaña validando contra los canales
    /// habilitados del agente. Retorna (resolvedChannel, null) si OK, o
    /// (default, errorResult) si hay validación pendiente.
    /// </summary>
    private async Task<(ChannelType Channel, IActionResult? Error)>
        ResolveCampaignChannelAsync(Guid agentId, string? requestedChannel, CancellationToken ct)
    {
        var agent = await db.AgentDefinitions
            .Where(a => a.Id == agentId && a.TenantId == tenantCtx.TenantId)
            .Select(a => new { a.Id, a.Name, a.EnabledChannels, a.IsActive })
            .FirstOrDefaultAsync(ct);

        if (agent is null)
            return (default, NotFound(new { error = "Agente no encontrado." }));
        if (!agent.IsActive)
            return (default, BadRequest(new { error = "El agente está inactivo." }));

        var enabled = agent.EnabledChannels ?? [];
        if (enabled.Count == 0)
        {
            return (default, BadRequest(new
            {
                error = $"El agente '{agent.Name}' no tiene canales habilitados. " +
                        "Editá el agente y habilitá al menos un canal antes de crear la campaña.",
                field = "agentId",
            }));
        }

        // 1 canal: usar ese sin importar el request
        if (enabled.Count == 1) return (enabled[0], null);

        // >1 canal: el request debe explicitar uno
        if (string.IsNullOrWhiteSpace(requestedChannel))
        {
            return (default, BadRequest(new
            {
                error = $"El agente tiene varios canales habilitados ({string.Join(", ", enabled)}). " +
                        "Especificá cuál usar para esta campaña.",
                field = "channel",
                availableChannels = enabled.Select(c => c.ToString()).ToArray(),
            }));
        }

        if (!Enum.TryParse<ChannelType>(requestedChannel, true, out var parsed))
        {
            return (default, BadRequest(new
            {
                error = $"Canal '{requestedChannel}' no es válido.",
                field = "channel",
            }));
        }

        if (!enabled.Contains(parsed))
        {
            return (default, BadRequest(new
            {
                error = $"El agente '{agent.Name}' no tiene habilitado el canal {parsed}. " +
                        $"Canales habilitados: {string.Join(", ", enabled)}.",
                field = "channel",
                availableChannels = enabled.Select(c => c.ToString()).ToArray(),
            }));
        }

        return (parsed, null);
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadAndStart(
        [FromForm] CampaignUploadRequest req,
        IFormFile file,
        CancellationToken ct)
    {
        var channelResolution = await ResolveCampaignChannelAsync(req.AgentId, req.Channel, ct);
        if (channelResolution.Error is not null) return channelResolution.Error;

        var contacts = new List<ContactRow>();
        var campaignId = await mediator.Send(new StartCampaignCommand(
            tenantCtx.TenantId,
            req.Name,
            req.AgentId,
            channelResolution.Channel,
            CampaignTrigger.FileUpload,
            contacts,
            CurrentUser,
            req.ScheduledAt
        ), ct);

        return Ok(new { campaignId });
    }

    /// <summary>
    /// Parsea un Excel en formato fijo y devuelve una vista previa de los contactos
    /// consolidados sin crear la campaña. Permite al usuario revisar los datos
    /// antes de confirmar.
    /// </summary>
    [HttpPost("preview-fixed")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public IActionResult PreviewFixed(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo vacío." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls" and not ".csv")
            return BadRequest(new { error = "Formato no soportado. Use Excel (.xlsx/.xls) o CSV." });

        FixedFormatParseResult parsed;
        using (var stream = file.OpenReadStream())
            parsed = fixedFormatService.Parse(stream, file.FileName);

        if (parsed.Warnings.Count > 0 && parsed.Contacts.Count == 0)
            return BadRequest(new { error = parsed.Warnings[0], warnings = parsed.Warnings });

        var preview = parsed.Contacts.Select(c =>
        {
            // Extraer NombreCliente y KeyValue del primer registro del JSON
            string nombreCliente = c.ClientName ?? "";
            string keyValue = "";
            if (c.ContactDataJson is not null)
            {
                try
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(c.ContactDataJson);
                    if (arr?.Count > 0)
                    {
                        keyValue = arr[0].GetValueOrDefault("KeyValue")?.ToString() ?? "";
                    }
                }
                catch { }
            }
            return new
            {
                phone = c.PhoneNumber,
                nombreCliente,
                keyValue,
                totalRegistros = GetRegistroCount(c.ContactDataJson),
                contactDataJson = c.ContactDataJson,
            };
        }).ToList();

        return Ok(new
        {
            contacts = preview,
            totalRowsRead = parsed.TotalRowsRead,
            extraColumns = parsed.ExtraColumns,
            warnings = parsed.Warnings,
        });
    }

    private static int GetRegistroCount(string? json)
    {
        if (json is null) return 1;
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            return arr.ValueKind == System.Text.Json.JsonValueKind.Array ? arr.GetArrayLength() : 1;
        }
        catch { return 1; }
    }

    /// <summary>
    /// Crea una campaña a partir de un Excel en formato fijo.
    ///
    /// Columnas requeridas: NombreCliente | Celular | CodigoPais | KeyValue
    /// Columnas adicionales variables: se capturan automáticamente en ContactDataJson.
    ///
    /// Múltiples filas del mismo número de teléfono se consolidan en un único
    /// contacto con un array "registros" en ContactDataJson.
    /// </summary>
    [HttpPost("upload-fixed")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadFixed(
        [FromForm] UploadFixedFormatRequest req,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo vacío." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "El archivo excede el límite de 10 MB." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls" and not ".csv")
            return BadRequest(new { error = "Formato no soportado. Use Excel (.xlsx/.xls) o CSV." });

        FixedFormatParseResult parsed;
        using (var stream = file.OpenReadStream())
            parsed = fixedFormatService.Parse(stream, file.FileName);

        // Si hay errores de columnas faltantes los warnings lo indican
        if (parsed.Contacts.Count == 0)
            return BadRequest(new
            {
                error = "No se encontraron contactos válidos en el archivo.",
                warnings = parsed.Warnings
            });

        // Validar/resolver canal contra los habilitados del agente.
        var channelResolution = await ResolveCampaignChannelAsync(req.AgentId, req.Channel, ct);
        if (channelResolution.Error is not null) return channelResolution.Error;

        var campaignId = await mediator.Send(new StartCampaignCommand(
            tenantCtx.TenantId,
            req.Name,
            req.AgentId,
            channelResolution.Channel,
            CampaignTrigger.FileUpload,
            parsed.Contacts,
            CurrentUser,
            req.ScheduledAt,
            req.CampaignTemplateId
        ), ct);

        return Ok(new
        {
            campaignId,
            contactCount = parsed.Contacts.Count,
            totalRowsRead = parsed.TotalRowsRead,
            extraColumns = parsed.ExtraColumns,
            warnings = parsed.Warnings
        });
    }

    /// <summary>
    /// Lanza una campaña creada. Internamente delega al flujo v2 (CampaignWorker
    /// en el Worker Service); el frontend conserva la URL <c>/launch</c> para no
    /// requerir cambios en el cliente. Mantiene shape de respuesta compatible con
    /// el contrato anterior (campaignId, status, pendingContacts, launchedAt).
    /// </summary>
    [HttpPost("{id:guid}/launch")]
    public async Task<IActionResult> Launch(Guid id, CancellationToken ct)
    {
        var lineCheck = await ValidateWhatsAppLineForLaunchAsync(id, tenantCtx.TenantId, ct);
        if (lineCheck is not null)
            return BadRequest(new { error = lineCheck });

        var userIdStr = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                     ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        string? launcherPhone = null;
        if (Guid.TryParse(userIdStr, out var launcherId))
        {
            var launcher = await db.AppUsers.FindAsync([launcherId], ct);
            launcherPhone = launcher?.NotifyPhone;
        }

        var result = await mediator.Send(new LaunchCampaignV2Command(
            CampaignId: id,
            TenantId: tenantCtx.TenantId,
            LaunchedByUserId: CurrentUser,
            LaunchedByUserPhone: launcherPhone,
            WarmupDay: 0
        ), ct);

        if (!result.Success)
        {
            // 404 si la campaña no pertenece al tenant; 400 para el resto de fallos.
            if (string.Equals(result.Status, "NotFound", StringComparison.Ordinal))
                return NotFound(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }

        return Ok(new
        {
            campaignId      = result.CampaignId,
            status          = result.Status,
            pendingContacts = result.QueuedCount + result.DeferredCount,
            launchedAt      = result.LaunchedAt,
        });
    }

    private static List<ContactRow> ParseWithMapping(Stream stream, Dictionary<string, string> mapping)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var ws = workbook.Worksheets.First();
        var range = ws.RangeUsed();
        if (range is null) return [];

        var lastRow = range.LastRow().RowNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        var headers = new List<string>();
        for (var col = 1; col <= lastCol; col++)
            headers.Add(ws.Cell(1, col).GetString().Trim());

        // Si no viene mapping explícito, detectar automáticamente por nombre de columna
        if (mapping is null || mapping.Count == 0)
            mapping = AutoDetectMapping(headers);

        var contacts = new List<ContactRow>();
        for (var row = 2; row <= lastRow; row++)
        {
            var rowData = new Dictionary<string, string>();
            for (var col = 0; col < headers.Count; col++)
                rowData[headers[col]] = ws.Cell(row, col + 1).GetString().Trim();

            string GetMapped(string field) =>
                mapping.FirstOrDefault(m => m.Value == field).Key is { } sourceCol
                    ? rowData.GetValueOrDefault(sourceCol, "")
                    : "";

            var phone = GetMapped("phone");
            if (string.IsNullOrWhiteSpace(phone)) continue;

            var mappedSourceCols = mapping.Keys.ToHashSet();
            // Todo lo que no fue mapeado a un campo conocido va a ContactDataJson
            var extra = rowData
                .Where(kv => !mappedSourceCols.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var pendingStr = GetMapped("pendingAmount");
            decimal? pendingAmount = decimal.TryParse(
                pendingStr?.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var pa) ? pa : null;

            contacts.Add(new ContactRow(
                PhoneNumber: phone,
                Email: GetMapped("email"),
                ClientName: GetMapped("clientName"),
                PolicyNumber: GetMapped("policyNumber"),
                InsuranceCompany: GetMapped("insuranceCompany"),
                PendingAmount: pendingAmount,
                Extra: extra
            ));
        }

        return contacts;
    }

    /// <summary>
    /// Detecta automáticamente qué columna del Excel corresponde a cada campo interno.
    /// Soporta nombres en español e inglés, con y sin tildes.
    /// El campo detectado es el nombre de la columna en el Excel; el valor es el campo interno.
    /// </summary>
    private static Dictionary<string, string> AutoDetectMapping(List<string> headers)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Alias por campo interno — orden de prioridad: primero el más específico
        var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["phone"] =
            [
                "celular", "telefono", "teléfono", "phone", "tel", "movil", "móvil",
                "whatsapp", "numero", "número", "cel", "numerodecell", "numerodecelular",
                "numerodecliente", "numerocliente"
            ],
            ["clientName"] =
            [
                "nombrecliente", "nombre_cliente", "nombre", "cliente", "name",
                "clientname", "razonsocial", "razón social", "nombredel cliente",
                "nombrecompleto", "nombresapellidos"
            ],
            ["email"] =
            [
                "email", "correo", "correoelectronico", "correo electronico",
                "correo_electronico", "e-mail", "emailcliente"
            ],
            ["policyNumber"] =
            [
                "poliza", "póliza", "numeropoliza", "númeropoliza", "numerodepoliza",
                "nopoliza", "policy", "policynumber", "numpoliza", "npoliza"
            ],
            ["insuranceCompany"] =
            [
                "aseguradora", "seguro", "compania", "compañia", "compañía",
                "insurance", "insurancecompany", "asegurado", "aseguradoracompania"
            ],
            ["pendingAmount"] =
            [
                "monto", "montodeuda", "deuda", "saldo", "balance", "amount",
                "pendingamount", "montoavencer", "montopendiente", "prima",
                "totaldeuda", "totalapagar"
            ],
        };

        foreach (var header in headers)
        {
            var normalized = header.ToLowerInvariant()
                .Replace(" ", "").Replace("_", "").Replace("-", "")
                .Replace("á","a").Replace("é","e").Replace("í","i")
                .Replace("ó","o").Replace("ú","u").Replace("ñ","n");

            foreach (var (field, aliasList) in aliases)
            {
                // Ya mapeamos este campo — no sobreescribir
                if (mapping.ContainsValue(field)) continue;

                var matched = aliasList.Any(alias =>
                {
                    var normAlias = alias.ToLowerInvariant()
                        .Replace(" ", "").Replace("_", "").Replace("-", "")
                        .Replace("á","a").Replace("é","e").Replace("í","i")
                        .Replace("ó","o").Replace("ú","u").Replace("ñ","n");
                    return normalized == normAlias || normalized.Contains(normAlias) || normAlias.Contains(normalized);
                });

                if (matched)
                {
                    mapping[header] = field;
                    break;
                }
            }
        }

        return mapping;
    }

    /// <summary>
    /// Sincroniza el estado real de entrega de cada CampaignContact contra
    /// UltraMsg. Útil para campañas que se lanzaron ANTES de activar el
    /// webhook on_ack — sin esto la BD muestra todos los contactos como
    /// "Sent" aunque WhatsApp haya descartado algunos por restricción.
    ///
    /// Flujo:
    /// 1. Resuelve la línea WhatsApp del tenant.
    /// 2. Llama UltraMsg /messages?status=X para cada status problemático
    ///    (queue, invalid, failed, expired, unsent).
    /// 3. Cruza por ExternalMessageId con los CampaignContacts de ESTA campaña.
    /// 4. Actualiza DeliveryStatus y, si es no-entregado, fuerza DispatchStatus=Error.
    /// 5. Devuelve summary agregado para mostrar en la modal.
    /// </summary>
    [HttpPost("{id:guid}/sync-delivery-status")]
    public async Task<IActionResult> SyncDeliveryStatus(Guid id, CancellationToken ct)
    {
        // 1. Cargar la campaña y validar tenant
        var campaign = await campaignRepo.GetByIdAsync(id, tenantCtx.TenantId, ct);
        if (campaign is null) return NotFound(new { error = "Campaña no encontrada." });

        // 2. Buscar línea UltraMsg activa del tenant
        var line = await db.WhatsAppLines
            .Where(l => l.TenantId == tenantCtx.TenantId && l.IsActive)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (line is null)
            return BadRequest(new { error = "El tenant no tiene una línea WhatsApp activa configurada." });

        var instanceNumber = (line.InstanceId ?? "").Replace("instance", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrEmpty(instanceNumber) || string.IsNullOrEmpty(line.ApiToken))
            return BadRequest(new { error = "La línea WhatsApp no tiene InstanceId o ApiToken configurados." });

        // 3. Cargar TODOS los CampaignContacts de esta campaña con ExternalMessageId
        var contacts = await db.CampaignContacts
            .Where(c => c.CampaignId == id && c.ExternalMessageId != null)
            .ToListAsync(ct);
        var byExternalId = contacts.ToDictionary(c => c.ExternalMessageId!, c => c);

        // 4. Para cada status REAL de UltraMsg, llamar API y cruzar.
        //    UltraMsg statuses válidos (confirmados vía /messages/statistics):
        //    queue, invalid, expired, unsent — son los que indican "no entregado".
        //    'sent' = aceptado por servidor WhatsApp (puede haber sido leído o no).
        //    'failed' NO ES un status válido de UltraMsg — si lo pasamos como filtro,
        //    UltraMsg devuelve TODOS los mensajes sin filtrar (bug detectado 2026-05-18).
        var statusesToFetch = new[] { "queue", "invalid", "expired", "unsent" };
        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);

        var updated = new List<object>();
        var counts = new Dictionary<string, int>
        {
            ["queue"]=0, ["invalid"]=0, ["expired"]=0, ["unsent"]=0,
        };

        foreach (var status in statusesToFetch)
        {
            var url = $"https://api.ultramsg.com/instance{instanceNumber}/messages" +
                      $"?token={Uri.EscapeDataString(line.ApiToken)}&status={status}&limit=500";
            HttpResponseMessage? resp = null;
            try { resp = await http.GetAsync(url, ct); }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = $"Error consultando UltraMsg ({status}): {ex.Message}" });
            }
            if (!resp.IsSuccessStatusCode) continue;

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("messages", out var messagesArr)) continue;

            foreach (var m in messagesArr.EnumerateArray())
            {
                // Validación defensiva: UltraMsg a veces devuelve mensajes que no
                // pertenecen al status pedido (bug del filtro). Verificamos el
                // status declarado en el propio payload antes de procesar.
                if (m.TryGetProperty("status", out var msgStatus)
                    && !string.Equals(msgStatus.GetString(), status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var extId = m.GetProperty("id").ToString();
                if (!byExternalId.TryGetValue(extId, out var cc)) continue;

                var previous = cc.DeliveryStatus;
                cc.DeliveryStatus = status;
                cc.DispatchStatus = AgentFlow.Domain.Enums.DispatchStatus.Error;
                cc.DispatchError ??= $"UltraMsg status={status} (sync)";
                counts[status]++;
                updated.Add(new
                {
                    contactId   = cc.Id,
                    clientName  = cc.ClientName,
                    phoneNumber = cc.PhoneNumber,
                    externalId  = extId,
                    previous,
                    newStatus   = status,
                });
            }
        }

        if (updated.Count > 0) await db.SaveChangesAsync(ct);

        // 5. Construir summary final desde BD (después del update).
        //    Para los `delivered`/`read` confiamos en lo que el webhook puso
        //    previamente. Para los `sent`, asumimos todos los que NO están
        //    en las 5 categorías de falla y tienen SentAt poblado.
        var summary = await db.CampaignContacts
            .Where(c => c.CampaignId == id)
            .GroupBy(c => 1)
            .Select(g => new
            {
                total       = g.Count(),
                read        = g.Count(c => c.DeliveryStatus == "read"),
                delivered   = g.Count(c => c.DeliveryStatus == "delivered"),
                queue       = g.Count(c => c.DeliveryStatus == "queue"),
                invalid     = g.Count(c => c.DeliveryStatus == "invalid"),
                failed      = g.Count(c => c.DeliveryStatus == "failed"),
                expired     = g.Count(c => c.DeliveryStatus == "expired"),
                unsent      = g.Count(c => c.DeliveryStatus == "unsent"),
                // "sent_or_unknown" = SentAt no nulo pero sin DeliveryStatus específico
                sentNoTracking = g.Count(c => c.SentAt != null && c.DeliveryStatus == null),
                pending     = g.Count(c => c.SentAt == null
                                          && c.DispatchStatus != AgentFlow.Domain.Enums.DispatchStatus.Error),
                error       = g.Count(c => c.DispatchStatus == AgentFlow.Domain.Enums.DispatchStatus.Error),
            })
            .FirstOrDefaultAsync(ct) ?? new
            {
                total = 0, read = 0, delivered = 0, queue = 0, invalid = 0,
                failed = 0, expired = 0, unsent = 0, sentNoTracking = 0,
                pending = 0, error = 0,
            };

        return Ok(new
        {
            campaignId   = id,
            campaignName = campaign.Name,
            syncedAt     = DateTime.UtcNow,
            updatedCount = updated.Count,
            summary,
            // Lista detallada de los que cambiaron — top 50 para no inflar el payload
            details      = updated.Take(50),
        });
    }
}
