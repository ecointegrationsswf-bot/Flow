namespace AgentFlow.Domain.Entities;

/// <summary>
/// Registro de números de teléfono que NO tienen cuenta activa en WhatsApp,
/// detectados por una combinación de:
///   1. Validación previa al envío vía UltraMsg /contacts/check (status="invalid").
///   2. Errores recurrentes durante el dispatch (DispatchError con "invalid", "not connected").
///   3. Carga manual por un administrador (ej: "no contactar nunca a este cliente").
///
/// Funciona como filtro defensivo cross-tenant para que ninguna campaña gaste
/// envíos a números que ya sabemos que no son entregables. Esto:
///   - Reduce el % de fallos por batch (evita que CampaignAutoPauseFailureRate dispare).
///   - Protege la reputación de la línea WhatsApp (menos rechazos en cadena).
///   - Mejora la métrica real de efectividad (denominador limpio).
///
/// Se considera "cross-tenant" por defecto (TenantId = null): si un número no
/// existe en WhatsApp para Somos Seguros, tampoco existe para PASESA. Pero el
/// admin puede agregar entradas con TenantId específico si quiere bloquear el
/// número solo para un corredor (ej: "este cliente cambió de aseguradora").
/// </summary>
public class InvalidWhatsAppNumber
{
    public Guid Id { get; set; }

    /// <summary>Teléfono en formato E.164 (+507XXXXXXXX). UNIQUE en BD.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Razón legible para humanos. Ejemplos:
    /// "No registrado en WhatsApp (UltraMsg /contacts/check)",
    /// "Errores recurrentes en envíos previos (3+ veces 'invalid')",
    /// "Cliente solicitó no recibir más mensajes",
    /// "Número de empresa cerrada".
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Cómo se detectó. Valores típicos:
    /// "ultramsg-precheck"  → API check antes del envío.
    /// "dispatch-error"     → mensaje envió y UltraMsg respondió error.
    /// "manual"             → admin lo agregó desde el panel.
    /// "campaign-import"    → marcado durante la carga del archivo (lista negra previa).
    /// </summary>
    public string Source { get; set; } = string.Empty;

    public DateTime FirstDetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Cuántas veces se ha detectado / reintentado. Cada vez sube +1.</summary>
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>
    /// Null = aplica cross-tenant (default). Si es no-null, solo aplica a ese tenant
    /// (caso: cliente que migró de un corredor a otro y no quiere mensajes del primero).
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>Último tenant que detectó / intentó este número. Solo para auditoría.</summary>
    public Guid? LastTenantId { get; set; }

    /// <summary>Última campaña que intentó este número. Solo para auditoría.</summary>
    public Guid? LastCampaignId { get; set; }

    /// <summary>Notas libres del admin si lo agrega manualmente.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Soft delete: si un admin verifica que el número SÍ es válido (falso positivo),
    /// se desactiva en lugar de borrar. Así mantenemos auditoría. Las queries del
    /// validator solo consideran IsActive=true.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Usuario que registró el número (cuando es manual o desactivación).</summary>
    public Guid? CreatedByUserId { get; set; }
    public Guid? DeactivatedByUserId { get; set; }
    public DateTime? DeactivatedAt { get; set; }
}
