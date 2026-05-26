using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Validador de números de teléfono para WhatsApp con dos capas:
///   1. Consulta de lista negra histórica (InvalidWhatsAppNumbers) — barato, sin red.
///   2. Verificación contra UltraMsg /contacts/check — confirmación en tiempo real
///      antes de gastar UN envío.
///
/// El método combinado <see cref="ValidateBeforeSendAsync"/> ejecuta ambas en orden:
/// si la lista negra ya lo tiene, devuelve "blacklisted" sin tocar la red. Si no,
/// pregunta a UltraMsg; si responde inválido, registra en la lista y devuelve.
///
/// Diseñado para evitar el escenario del 18-may: UltraMsg encolando mensajes
/// hacia números sin WhatsApp y luego disparándolos en ráfaga al reconectar.
/// </summary>
public interface IWhatsAppNumberValidator
{
    /// <summary>
    /// Consulta SOLO la lista negra local. Útil al cargar el archivo para
    /// pre-filtrar antes de procesar (no toca UltraMsg, no genera latencia).
    /// </summary>
    Task<InvalidWhatsAppNumberInfo?> CheckBlacklistAsync(
        string phoneNumber, Guid? tenantId, CancellationToken ct);

    /// <summary>
    /// Validación completa: lista negra + UltraMsg /contacts/check.
    /// Si UltraMsg responde inválido, registra en la lista negra automáticamente.
    /// </summary>
    Task<WhatsAppNumberValidationResult> ValidateBeforeSendAsync(
        string phoneNumber, WhatsAppLine line, Guid? tenantId, Guid? campaignId,
        CancellationToken ct);

    /// <summary>
    /// Registra (o actualiza el contador de) un número en la lista negra. Idempotente:
    /// si ya existe la entrada (mismo phone + tenant), incrementa OccurrenceCount y
    /// LastCheckedAt en lugar de duplicar.
    /// </summary>
    Task RegisterAsBlacklistedAsync(
        string phoneNumber, string reason, string source,
        Guid? tenantId, Guid? campaignId, Guid? userId,
        CancellationToken ct);

    /// <summary>
    /// Restaura (IsActive=false → no aplicar) un número de la lista. Soft-delete:
    /// el registro se mantiene para auditoría.
    /// </summary>
    Task<bool> RestoreAsync(Guid id, Guid userId, CancellationToken ct);
}

/// <summary>Resultado simplificado de la consulta a la lista negra.</summary>
public record InvalidWhatsAppNumberInfo(Guid Id, string Reason, string Source, DateTime FirstDetectedAt, int OccurrenceCount);

/// <summary>Resultado de la validación combinada.</summary>
public record WhatsAppNumberValidationResult(
    bool IsValid,
    /// <summary>Origen del veredicto: "ok", "blacklist", "ultramsg-invalid", "error".</summary>
    string Source,
    /// <summary>Razón legible (para mostrar al admin o registrar en DispatchError).</summary>
    string? Reason);
