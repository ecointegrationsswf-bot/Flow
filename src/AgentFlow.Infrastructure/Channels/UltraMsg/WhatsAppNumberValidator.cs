using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.UltraMsg;

/// <summary>
/// Implementación principal de IWhatsAppNumberValidator que combina:
///   - InvalidWhatsAppNumbers (lista negra histórica, lectura SQL)
///   - UltraMsg /contacts/check (validación en tiempo real)
///   - IMemoryCache (24h) para no re-validar el mismo número múltiples veces
///     dentro del mismo día.
/// </summary>
public class WhatsAppNumberValidator(
    AgentFlowDbContext db,
    UltraMsgContactsChecker checker,
    IMemoryCache cache,
    ILogger<WhatsAppNumberValidator> logger) : IWhatsAppNumberValidator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public async Task<InvalidWhatsAppNumberInfo?> CheckBlacklistAsync(
        string phoneNumber, Guid? tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return null;

        // 1. Primero busca entrada específica del tenant.
        if (tenantId.HasValue)
        {
            var tenantEntry = await db.InvalidWhatsAppNumbers
                .Where(x => x.PhoneNumber == phoneNumber && x.TenantId == tenantId && x.IsActive)
                .Select(x => new InvalidWhatsAppNumberInfo(x.Id, x.Reason, x.Source, x.FirstDetectedAt, x.OccurrenceCount))
                .FirstOrDefaultAsync(ct);
            if (tenantEntry is not null) return tenantEntry;
        }

        // 2. Si no, busca entrada cross-tenant (TenantId NULL).
        var globalEntry = await db.InvalidWhatsAppNumbers
            .Where(x => x.PhoneNumber == phoneNumber && x.TenantId == null && x.IsActive)
            .Select(x => new InvalidWhatsAppNumberInfo(x.Id, x.Reason, x.Source, x.FirstDetectedAt, x.OccurrenceCount))
            .FirstOrDefaultAsync(ct);
        return globalEntry;
    }

    public async Task<WhatsAppNumberValidationResult> ValidateBeforeSendAsync(
        string phoneNumber, WhatsAppLine line, Guid? tenantId, Guid? campaignId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return new(false, "error", "Teléfono vacío.");

        // ── Capa 1 — Lista negra histórica (sin red) ─────────────
        var blacklisted = await CheckBlacklistAsync(phoneNumber, tenantId, ct);
        if (blacklisted is not null)
        {
            // Subimos el contador para saber cuántas campañas adicionales lo intentaron.
            await IncrementOccurrenceAsync(phoneNumber, tenantId, campaignId, ct);
            return new(false, "blacklist",
                $"Número en lista negra ({blacklisted.OccurrenceCount + 1} intentos): {blacklisted.Reason}");
        }

        // ── Capa 2 — Cache positiva (validado <24h) ──────────────
        // Solo cacheamos VÁLIDOS positivos. Inválidos van a la BD (caso pesimista).
        var cacheKey = $"wa:valid:{phoneNumber}";
        if (cache.TryGetValue<bool>(cacheKey, out var cachedValid) && cachedValid)
            return new(true, "cache", null);

        // ── Capa 3 — UltraMsg /contacts/check ────────────────────
        var existsInWa = await checker.ExistsInWhatsAppAsync(line.InstanceId, line.ApiToken, phoneNumber, ct);

        if (existsInWa == true)
        {
            cache.Set(cacheKey, true, CacheTtl);
            return new(true, "ultramsg-valid", null);
        }

        if (existsInWa == false)
        {
            // Registramos en lista negra del TENANT (no cross-tenant).
            // Razón: datasets distintos por corredor — el mismo teléfono puede
            // estar mal escrito para Somos pero correcto para PASESA. Quien
            // suba el archivo es responsable de su universo. Un admin puede
            // elevar manualmente a "global" desde el panel si tiene razón
            // operativa (ej: número reportado por baja general).
            await RegisterAsBlacklistedAsync(
                phoneNumber,
                reason: "No registrado en WhatsApp (UltraMsg /contacts/check status=invalid)",
                source: "ultramsg-precheck",
                tenantId: tenantId,
                campaignId: campaignId,
                userId: null,
                ct: ct);
            return new(false, "ultramsg-invalid",
                "El número no tiene cuenta activa en WhatsApp.");
        }

        // Null = no determinable. Fail-open: permitimos el envío y dejamos que el
        // dispatch real falle si corresponde. Loggeamos para visibilidad.
        logger.LogWarning(
            "[WANumberValidator] No se pudo verificar {Phone} en UltraMsg (timeout/error). Fail-open.",
            phoneNumber);
        return new(true, "ultramsg-unknown",
            "No se pudo verificar — se permite el envío (fail-open).");
    }

    public async Task RegisterAsBlacklistedAsync(
        string phoneNumber, string reason, string source,
        Guid? tenantId, Guid? campaignId, Guid? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return;

        var existing = await db.InvalidWhatsAppNumbers
            .Where(x => x.PhoneNumber == phoneNumber && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            // Idempotente: si la entrada ya existe, actualizamos contador + timestamps.
            existing.OccurrenceCount++;
            existing.LastCheckedAt = DateTime.UtcNow;
            if (campaignId.HasValue) existing.LastCampaignId = campaignId;
            if (tenantId.HasValue) existing.LastTenantId = tenantId;
            // Si estaba desactivado y se vuelve a detectar, lo reactivamos (algo cambió).
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                existing.Notes = $"{existing.Notes}\n[Re-detectado {DateTime.UtcNow:yyyy-MM-dd}]".Trim();
            }
        }
        else
        {
            db.InvalidWhatsAppNumbers.Add(new InvalidWhatsAppNumber
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                Reason = reason,
                Source = source,
                FirstDetectedAt = DateTime.UtcNow,
                LastCheckedAt = DateTime.UtcNow,
                OccurrenceCount = 1,
                TenantId = tenantId,
                LastTenantId = tenantId,
                LastCampaignId = campaignId,
                CreatedByUserId = userId,
                IsActive = true,
            });
        }

        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex)
        {
            // Carrera entre dos requests del dispatcher para el mismo número: el unique
            // constraint en (PhoneNumber, TenantId) tira excepción. La tratamos como
            // benigna — el otro request ganó la inserción.
            logger.LogDebug(ex, "[WANumberValidator] Insert race para {Phone} — ignorando.", phoneNumber);
        }
    }

    public async Task<bool> RestoreAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var entity = await db.InvalidWhatsAppNumbers.FindAsync(new object[] { id }, ct);
        if (entity is null || !entity.IsActive) return false;
        entity.IsActive = false;
        entity.DeactivatedByUserId = userId;
        entity.DeactivatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task IncrementOccurrenceAsync(
        string phoneNumber, Guid? tenantId, Guid? campaignId, CancellationToken ct)
    {
        try
        {
            await db.InvalidWhatsAppNumbers
                .Where(x => x.PhoneNumber == phoneNumber && (x.TenantId == tenantId || x.TenantId == null) && x.IsActive)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.OccurrenceCount, x => x.OccurrenceCount + 1)
                    .SetProperty(x => x.LastCheckedAt, _ => DateTime.UtcNow)
                    .SetProperty(x => x.LastCampaignId, _ => campaignId)
                    .SetProperty(x => x.LastTenantId, _ => tenantId), ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[WANumberValidator] Increment failed for {Phone}.", phoneNumber);
        }
    }
}
