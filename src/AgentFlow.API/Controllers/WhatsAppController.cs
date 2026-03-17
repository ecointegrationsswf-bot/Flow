using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
// [Authorize] // TODO: habilitar cuando auth esté configurado
[Route("api/whatsapp")]
public class WhatsAppController(
    ITenantContext tenantCtx,
    IUltraMsgInstanceService ultraMsg,
    AgentFlowDbContext db) : ControllerBase
{
    /// <summary>
    /// Obtiene el estado de la instancia UltraMsg del tenant actual.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantCtx.TenantId, ct);

        if (tenant is null)
            return NotFound(new { error = "Tenant no encontrado" });

        if (string.IsNullOrEmpty(tenant.WhatsAppInstanceId) || string.IsNullOrEmpty(tenant.WhatsAppApiToken))
            return BadRequest(new { error = "Instancia UltraMsg no configurada para este tenant" });

        try
        {
            var status = await ultraMsg.GetStatusAsync(tenant.WhatsAppInstanceId, tenant.WhatsAppApiToken, ct);
            return Ok(new
            {
                status = status.Status,
                phone = tenant.WhatsAppPhoneNumber,
                instanceId = tenant.WhatsAppInstanceId,
                provider = tenant.WhatsAppProvider.ToString()
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = $"Error al conectar con UltraMsg: {ex.Message}" });
        }
    }

    /// <summary>
    /// Retorna la imagen QR para autenticación de WhatsApp.
    /// </summary>
    [HttpGet("qr")]
    public async Task<IActionResult> GetQrCode(CancellationToken ct)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantCtx.TenantId, ct);

        if (tenant is null)
            return NotFound(new { error = "Tenant no encontrado" });

        if (string.IsNullOrEmpty(tenant.WhatsAppInstanceId) || string.IsNullOrEmpty(tenant.WhatsAppApiToken))
            return BadRequest(new { error = "Instancia UltraMsg no configurada" });

        try
        {
            var qrBytes = await ultraMsg.GetQrCodeAsync(tenant.WhatsAppInstanceId, tenant.WhatsAppApiToken, ct);
            return File(qrBytes, "image/png");
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = $"Error al obtener QR: {ex.Message}" });
        }
    }

    /// <summary>
    /// Reinicia la instancia UltraMsg del tenant.
    /// </summary>
    [HttpPost("restart")]
    public async Task<IActionResult> Restart(CancellationToken ct)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantCtx.TenantId, ct);

        if (tenant is null)
            return NotFound(new { error = "Tenant no encontrado" });

        if (string.IsNullOrEmpty(tenant.WhatsAppInstanceId) || string.IsNullOrEmpty(tenant.WhatsAppApiToken))
            return BadRequest(new { error = "Instancia UltraMsg no configurada" });

        try
        {
            var success = await ultraMsg.RestartAsync(tenant.WhatsAppInstanceId, tenant.WhatsAppApiToken, ct);
            return success
                ? Ok(new { message = "Instancia reiniciada correctamente" })
                : StatusCode(502, new { error = "No se pudo reiniciar la instancia" });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = $"Error al reiniciar: {ex.Message}" });
        }
    }

    /// <summary>
    /// Cierra sesión de WhatsApp para generar un nuevo QR.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantCtx.TenantId, ct);

        if (tenant is null)
            return NotFound(new { error = "Tenant no encontrado" });

        if (string.IsNullOrEmpty(tenant.WhatsAppInstanceId) || string.IsNullOrEmpty(tenant.WhatsAppApiToken))
            return BadRequest(new { error = "Instancia UltraMsg no configurada" });

        try
        {
            var success = await ultraMsg.LogoutAsync(tenant.WhatsAppInstanceId, tenant.WhatsAppApiToken, ct);
            return success
                ? Ok(new { message = "Sesion cerrada. Escanea el nuevo QR para reconectar." })
                : StatusCode(502, new { error = "No se pudo cerrar la sesion" });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = $"Error al cerrar sesion: {ex.Message}" });
        }
    }
}
