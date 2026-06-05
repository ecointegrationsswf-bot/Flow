using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

// Provider: "UltraMsg" | "MetaCloudApi". Para Meta, InstanceId lleva el phone_number_id
// y ApiToken queda vacío (se usan los campos Me*). Los secretos (MetaAccessToken,
// MetaAppSecret) solo se sobre-escriben en update si vienen con valor (igual que ApiToken).
public record CreateWhatsAppLineRequest(
    string DisplayName, string PhoneNumber, string InstanceId, string? ApiToken = null,
    string? Provider = null, string? MetaWabaId = null, string? MetaAccessToken = null,
    string? MetaAppSecret = null, string? MetaBusinessId = null);
public record UpdateWhatsAppLineRequest(
    string DisplayName, string PhoneNumber, string? InstanceId, string? ApiToken, bool IsActive,
    string? Provider = null, string? MetaWabaId = null, string? MetaAccessToken = null,
    string? MetaAppSecret = null, string? MetaBusinessId = null);

[ApiController]
[Route("api/whatsapp-lines")]
[Microsoft.AspNetCore.Authorization.Authorize]
public class WhatsAppLinesController(
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    IUltraMsgInstanceService ultraMsg) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var lines = await db.WhatsAppLines
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        return Ok(lines.Select(Project));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWhatsAppLineRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        if (!Enum.TryParse<ProviderType>(req.Provider, ignoreCase: true, out var provider))
            provider = ProviderType.UltraMsg;

        // Validación por proveedor. En Meta, InstanceId lleva el phone_number_id.
        if (provider == ProviderType.UltraMsg)
        {
            if (string.IsNullOrWhiteSpace(req.InstanceId) || string.IsNullOrWhiteSpace(req.ApiToken))
                return BadRequest(new { error = "UltraMsg requiere Instance ID y Token." });
        }
        else // MetaCloudApi
        {
            if (string.IsNullOrWhiteSpace(req.InstanceId) || string.IsNullOrWhiteSpace(req.MetaWabaId) || string.IsNullOrWhiteSpace(req.MetaAccessToken))
                return BadRequest(new { error = "Meta requiere Phone Number ID, WABA ID y Access Token." });
        }

        // Validar que no exista otra linea con el mismo instanceId/phone_number_id en el tenant
        var exists = await db.WhatsAppLines
            .AnyAsync(l => l.TenantId == tenantId && l.InstanceId == req.InstanceId, ct);
        if (exists)
            return Conflict(new { error = "Ya existe una linea con ese Instance ID / Phone Number ID." });

        var line = new WhatsAppLine
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = req.DisplayName,
            PhoneNumber = req.PhoneNumber,
            InstanceId = req.InstanceId,
            ApiToken = req.ApiToken ?? string.Empty,
            Provider = provider,
            MetaWabaId = req.MetaWabaId,
            MetaAccessToken = req.MetaAccessToken,
            MetaAppSecret = req.MetaAppSecret,
            MetaBusinessId = req.MetaBusinessId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        db.WhatsAppLines.Add(line);
        await db.SaveChangesAsync(ct);

        return Ok(Project(line));
    }

    // Proyección de respuesta — nunca expone los secretos en claro (solo flags).
    private static object Project(WhatsAppLine line) => new
    {
        line.Id,
        line.TenantId,
        line.DisplayName,
        line.PhoneNumber,
        line.InstanceId,
        Provider = line.Provider.ToString(),
        line.MetaWabaId,
        line.MetaBusinessId,
        // Pista enmascarada (últimos 4) para que el usuario vea que el secreto SÍ
        // está guardado, sin exponerlo. null = no configurado.
        MetaAccessTokenLast4 = Last4(line.MetaAccessToken),
        MetaAppSecretLast4 = Last4(line.MetaAppSecret),
        line.IsActive,
        line.CreatedAt,
        line.UpdatedAt,
    };

    private static string? Last4(string? secret)
        => string.IsNullOrEmpty(secret) ? null : secret.Length <= 4 ? "****" : secret[^4..];

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWhatsAppLineRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var line = await db.WhatsAppLines
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);

        if (line is null)
            return NotFound(new { error = "Linea no encontrada." });

        line.DisplayName = req.DisplayName;
        line.PhoneNumber = req.PhoneNumber;
        line.IsActive = req.IsActive;

        if (Enum.TryParse<ProviderType>(req.Provider, ignoreCase: true, out var provider))
            line.Provider = provider;

        if (!string.IsNullOrWhiteSpace(req.InstanceId))
            line.InstanceId = req.InstanceId;
        if (!string.IsNullOrWhiteSpace(req.ApiToken))
            line.ApiToken = req.ApiToken;

        // Campos Meta no secretos — se actualizan si vienen con valor.
        if (!string.IsNullOrWhiteSpace(req.MetaWabaId))
            line.MetaWabaId = req.MetaWabaId;
        if (!string.IsNullOrWhiteSpace(req.MetaBusinessId))
            line.MetaBusinessId = req.MetaBusinessId;
        // Secretos — solo se sobre-escriben si el form envió un valor nuevo
        // (así el form puede dejarlos en blanco para conservar el actual).
        if (!string.IsNullOrWhiteSpace(req.MetaAccessToken))
            line.MetaAccessToken = req.MetaAccessToken;
        if (!string.IsNullOrWhiteSpace(req.MetaAppSecret))
            line.MetaAppSecret = req.MetaAppSecret;

        line.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(Project(line));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var line = await db.WhatsAppLines
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);

        if (line is null)
            return NotFound(new { error = "Linea no encontrada." });

        // Desvincula agentes que referencian esta línea antes de eliminarla
        var agentsUsingLine = await db.AgentDefinitions
            .Where(a => a.WhatsAppLineId == id)
            .ToListAsync(ct);

        foreach (var agent in agentsUsingLine)
            agent.WhatsAppLineId = null;

        db.WhatsAppLines.Remove(line);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Per-line UltraMsg operations ─────────────────────

    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var line = await GetLine(id, ct);
        if (line is null) return NotFound(new { error = "Linea no encontrada." });

        try
        {
            var status = await ultraMsg.GetStatusAsync(line.InstanceId, line.ApiToken, ct);

            // Actualizar telefono si UltraMsg lo reporta y es diferente
            if (!string.IsNullOrEmpty(status.Phone) && status.Phone != line.PhoneNumber)
            {
                line.PhoneNumber = status.Phone;
                line.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Ok(new
            {
                status = status.Status,
                phone = line.PhoneNumber,
                instanceId = line.InstanceId,
                lineId = line.Id,
                displayName = line.DisplayName,
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al conectar con UltraMsg." });
        }
    }

    [HttpGet("{id:guid}/qr")]
    public async Task<IActionResult> GetQrCode(Guid id, CancellationToken ct)
    {
        var line = await GetLine(id, ct);
        if (line is null) return NotFound(new { error = "Linea no encontrada." });

        try
        {
            var qrBytes = await ultraMsg.GetQrCodeAsync(line.InstanceId, line.ApiToken, ct);
            return File(qrBytes, "image/png");
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "QR no disponible para esta instancia." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al obtener QR." });
        }
    }

    [HttpPost("{id:guid}/restart")]
    public async Task<IActionResult> Restart(Guid id, CancellationToken ct)
    {
        var line = await GetLine(id, ct);
        if (line is null) return NotFound(new { error = "Linea no encontrada." });

        try
        {
            var success = await ultraMsg.RestartAsync(line.InstanceId, line.ApiToken, ct);
            return success
                ? Ok(new { message = "Instancia reiniciada correctamente" })
                : StatusCode(502, new { error = "No se pudo reiniciar la instancia" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al reiniciar la instancia." });
        }
    }

    [HttpPost("{id:guid}/logout")]
    public async Task<IActionResult> Logout(Guid id, CancellationToken ct)
    {
        var line = await GetLine(id, ct);
        if (line is null) return NotFound(new { error = "Linea no encontrada." });

        try
        {
            var success = await ultraMsg.LogoutAsync(line.InstanceId, line.ApiToken, ct);
            return success
                ? Ok(new { message = "Sesion cerrada. Escanea el nuevo QR para reconectar." })
                : StatusCode(502, new { error = "No se pudo cerrar la sesion" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al cerrar sesion." });
        }
    }

    /// <summary>
    /// Envía un mensaje de prueba desde una línea WhatsApp específica.
    /// Útil para verificar que la conexión UltraMsg está funcionando.
    /// </summary>
    [HttpPost("{id:guid}/test-message")]
    public async Task<IActionResult> SendTestMessage(
        Guid id,
        [FromBody] TestMessageRequest req,
        [FromServices] IChannelProviderFactory providerFactory,
        CancellationToken ct)
    {
        var line = await GetLine(id, ct);
        if (line is null) return NotFound(new { error = "Linea no encontrada." });

        var provider = await providerFactory.GetProviderByLineAsync(id, ct);
        if (provider is null)
            return BadRequest(new { error = "No se pudo crear el proveedor para esta linea. Verifica Instance ID y Token." });

        var result = await provider.SendMessageAsync(
            new Domain.Interfaces.SendMessageRequest(req.To, req.Message), ct);

        if (!result.Success)
            return BadRequest(new { error = $"Error al enviar: {result.Error}" });

        return Ok(new
        {
            message = "Mensaje de prueba enviado exitosamente.",
            externalId = result.ExternalMessageId,
            to = req.To,
        });
    }

    private async Task<WhatsAppLine?> GetLine(Guid id, CancellationToken ct)
    {
        return await db.WhatsAppLines
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantCtx.TenantId, ct);
    }
}

public record TestMessageRequest(string To, string Message);
