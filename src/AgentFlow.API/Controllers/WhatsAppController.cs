using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
[Authorize]
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
        var (instanceId, token, phone, error) = await ResolveCredentialsAsync(ct);
        if (error is not null) return BadRequest(new { error });

        try
        {
            var status = await ultraMsg.GetStatusAsync(instanceId!, token!, ct);
            return Ok(new
            {
                status = status.Status,
                phone,
                instanceId,
                provider = "UltraMsg"
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al conectar con UltraMsg." });
        }
    }

    /// <summary>
    /// Retorna la imagen QR para autenticación de WhatsApp.
    /// </summary>
    [HttpGet("qr")]
    public async Task<IActionResult> GetQrCode(CancellationToken ct)
    {
        var (instanceId, token, _, error) = await ResolveCredentialsAsync(ct);
        if (error is not null) return BadRequest(new { error });

        try
        {
            var qrBytes = await ultraMsg.GetQrCodeAsync(instanceId!, token!, ct);
            return File(qrBytes, "image/png");
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al obtener QR." });
        }
    }

    /// <summary>
    /// Reinicia la instancia UltraMsg del tenant.
    /// </summary>
    [HttpPost("restart")]
    public async Task<IActionResult> Restart(CancellationToken ct)
    {
        var (instanceId, token, _, error) = await ResolveCredentialsAsync(ct);
        if (error is not null) return BadRequest(new { error });

        try
        {
            var success = await ultraMsg.RestartAsync(instanceId!, token!, ct);
            return success
                ? Ok(new { message = "Instancia reiniciada correctamente" })
                : StatusCode(502, new { error = "No se pudo reiniciar la instancia" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al reiniciar la instancia." });
        }
    }

    /// <summary>
    /// Cierra sesión de WhatsApp para generar un nuevo QR.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var (instanceId, token, _, error) = await ResolveCredentialsAsync(ct);
        if (error is not null) return BadRequest(new { error });

        try
        {
            var success = await ultraMsg.LogoutAsync(instanceId!, token!, ct);
            return success
                ? Ok(new { message = "Sesion cerrada. Escanea el nuevo QR para reconectar." })
                : StatusCode(502, new { error = "No se pudo cerrar la sesion" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al cerrar sesion." });
        }
    }

    // Resuelve instanceId y token: primero Tenant fields, si vacíos busca en WhatsAppLines
    private async Task<(string? InstanceId, string? Token, string? Phone, string? Error)> ResolveCredentialsAsync(CancellationToken ct)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantCtx.TenantId, ct);

        if (tenant is null)
            return (null, null, null, "Tenant no encontrado");

        var instanceId = tenant.WhatsAppInstanceId;
        var token      = tenant.WhatsAppApiToken;
        var phone      = tenant.WhatsAppPhoneNumber;

        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(token))
        {
            var line = await db.WhatsAppLines
                .Where(l => l.TenantId == tenantCtx.TenantId && l.IsActive)
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (line is null)
                return (null, null, null, "No hay una línea WhatsApp activa configurada para este tenant");

            instanceId = line.InstanceId;
            token      = line.ApiToken;
            phone      = line.PhoneNumber;
        }

        return (instanceId, token, phone, null);
    }
}
