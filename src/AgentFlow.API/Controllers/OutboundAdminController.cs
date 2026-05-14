using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Monitoreo cross-tenant de mensajes SALIENTES (Messages con Direction=Outbound).
/// Hermano del <c>InboxAdminController</c>: ese ve la cola de mensajes ENTRANTES
/// (InboundMessageQueue), este ve lo que se envió hacia clientes — WhatsApp, Email, SMS.
/// SOLO super admin.
///
/// Útil para diagnosticar:
///   • Volumen de envíos por canal en el rango (KPIs).
///   • Fallos al enviar (Status=Failed con un error de provider).
///   • Auditar contenido enviado a un cliente puntual.
/// </summary>
[ApiController]
[Route("api/admin/outbox")]
[Authorize(Roles = "super_admin")]
public class OutboundAdminController(AgentFlowDbContext db) : ControllerBase
{
    /// <summary>
    /// KPIs agregados sobre Messages.Direction=Outbound en las últimas N horas
    /// (default 24h). Conteos por canal y por status — alimenta las tarjetas
    /// de la página /admin/outbox.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));

        // Solo salientes — los inbound ya tienen su propia pantalla de monitoreo.
        var baseQuery = db.Set<Message>()
            .Where(m => m.Direction == MessageDirection.Outbound && m.SentAt >= since);

        var totalSent = await baseQuery.CountAsync(ct);

        var byChannel = await baseQuery
            .GroupBy(m => m.Channel)
            .Select(g => new { channel = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var byStatus = await baseQuery
            .GroupBy(m => m.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);

        return Ok(new
        {
            sinceUtc   = since,
            totalSent,
            byChannel,
            byStatus,
        });
    }

    /// <summary>
    /// Listado paginado de mensajes salientes con filtros.
    /// Default: últimas 24h. Mismo patrón que InboxAdminController.Items —
    /// paginar primero sobre la tabla indexada por SentAt y luego JOIN al subset.
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> Items(
        [FromQuery] string? channel = null,        // "Email" | "WhatsApp" | "Sms" | null=todos
        [FromQuery] string? status = null,         // "Sent" | "Failed" | etc | null=todos
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string? recipient = null,      // teléfono o email parcial
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        var fromUtc = from?.ToUniversalTime() ?? DateTime.UtcNow.AddHours(-24);
        var toUtc   = to?.ToUniversalTime()   ?? DateTime.UtcNow;

        var q = db.Set<Message>()
            .Where(m => m.Direction == MessageDirection.Outbound
                     && m.SentAt >= fromUtc && m.SentAt <= toUtc);

        if (!string.IsNullOrEmpty(channel) && Enum.TryParse<ChannelType>(channel, true, out var ch))
            q = q.Where(m => m.Channel == ch);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<MessageStatus>(status, true, out var st))
            q = q.Where(m => m.Status == st);
        if (!string.IsNullOrEmpty(recipient))
        {
            var needle = recipient.Trim();
            // Recipient puede ser email, ClientPhone hereda en la Conversation.
            q = q.Where(m => (m.Recipient != null && m.Recipient.Contains(needle))
                          || m.Conversation.ClientPhone.Contains(needle));
        }
        // tenantId se aplica a través de la Conversation — Messages no tiene TenantId directo.
        if (tenantId.HasValue)
            q = q.Where(m => m.Conversation.TenantId == tenantId.Value);

        var total = await q.CountAsync(ct);

        var clampedTake = Math.Clamp(take, 1, 200);
        var clampedSkip = Math.Max(skip, 0);

        // Proyectamos los campos crudos a un DTO intermedio. NO mezclamos
        // Message.Channel (int) con Conversation.Channel (nvarchar/HasConversion<string>) en
        // el server-side LINQ — SQL Server rechaza el coalesce y devuelve
        // "Conversion failed when converting the nvarchar value 'WhatsApp' to data type int".
        var raw = await q
            .OrderByDescending(m => m.SentAt)
            .Skip(clampedSkip).Take(clampedTake)
            .Select(m => new
            {
                m.Id,
                m.ConversationId,
                tenantId        = m.Conversation.TenantId,
                tenantName      = db.Tenants.Where(t => t.Id == m.Conversation.TenantId).Select(t => t.Name).FirstOrDefault(),
                clientName      = m.Conversation.ClientName,
                clientPhone     = m.Conversation.ClientPhone,
                m.Recipient,
                msgChannel      = m.Channel,                 // nullable int (Email|WhatsApp|Sms)
                convChannelStr  = m.Conversation.Channel,    // string (HasConversion<string>) — fallback
                m.Status,
                m.Subject,
                m.Content,
                m.AgentName,
                campaignId      = m.Conversation.CampaignId,
                externalId      = m.ExternalMessageId,
                m.SentAt,
            })
            .ToListAsync(ct);

        // Combinamos en memoria — aquí ya tenemos los tipos nativos y el coalesce es trivial.
        var items = raw.Select(r => new
        {
            id             = r.Id,
            conversationId = r.ConversationId,
            tenantId       = r.tenantId,
            tenantName     = r.tenantName,
            clientName     = r.clientName,
            recipient      = string.IsNullOrEmpty(r.Recipient) ? r.clientPhone : r.Recipient,
            channel        = (r.msgChannel?.ToString()) ?? r.convChannelStr.ToString(),
            status         = r.Status.ToString(),
            subject        = r.Subject,
            preview        = r.Content.Length > 80 ? r.Content[..80] : r.Content,
            agentName      = r.AgentName,
            campaignId     = r.campaignId,
            externalId     = r.externalId,
            sentAt         = r.SentAt,
        });

        return Ok(new { total, take = clampedTake, skip = clampedSkip, fromUtc, toUtc, items });
    }

    /// <summary>
    /// Detalle de un mensaje saliente — incluye el contenido completo (sin truncar)
    /// para inspeccionar el HTML/texto que se envió al cliente.
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        // Mismo cuidado que en Items: no mezclar Message.Channel (int) con
        // Conversation.Channel (string) dentro del LINQ — SQL rechaza la conversión.
        var raw = await db.Set<Message>()
            .Where(m => m.Id == id && m.Direction == MessageDirection.Outbound)
            .Select(m => new
            {
                m.Id,
                m.ConversationId,
                tenantId       = m.Conversation.TenantId,
                tenantName     = db.Tenants.Where(t => t.Id == m.Conversation.TenantId).Select(t => t.Name).FirstOrDefault(),
                clientName     = m.Conversation.ClientName,
                clientPhone    = m.Conversation.ClientPhone,
                m.Recipient,
                msgChannel     = m.Channel,
                convChannelStr = m.Conversation.Channel,
                m.Status,
                m.Subject,
                m.Content,
                m.AgentName,
                campaignId     = m.Conversation.CampaignId,
                externalId     = m.ExternalMessageId,
                m.SentAt,
            })
            .FirstOrDefaultAsync(ct);

        if (raw is null) return NotFound(new { error = "Mensaje no encontrado." });

        return Ok(new
        {
            id             = raw.Id,
            conversationId = raw.ConversationId,
            tenantId       = raw.tenantId,
            tenantName     = raw.tenantName,
            clientName     = raw.clientName,
            clientPhone    = raw.clientPhone,
            recipient      = raw.Recipient,
            channel        = (raw.msgChannel?.ToString()) ?? raw.convChannelStr.ToString(),
            status         = raw.Status.ToString(),
            subject        = raw.Subject,
            content        = raw.Content,
            agentName      = raw.AgentName,
            campaignId     = raw.campaignId,
            externalId     = raw.externalId,
            sentAt         = raw.SentAt,
        });
    }
}
