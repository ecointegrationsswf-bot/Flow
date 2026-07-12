namespace AgentFlow.Domain.Entities;

/// <summary>
/// Integración Ludo CRM — Fase 1. Mapeo idempotente entre un tenant de Ludo y el
/// tenant correspondiente en TalkIA. Es la CLAVE DE IDEMPOTENCIA del aprovisionamiento:
/// si Ludo reenvía el evento de creación, el provisioning encuentra esta fila y devuelve
/// el tenantId ya creado en vez de duplicar.
///
/// Es una tabla GLOBAL (no por tenant): vive en el catálogo compartido junto a Tenants.
/// TalkIA NO guarda IDs de prospecto ni de oportunidad — Ludo es la fuente de verdad y se
/// resuelve por teléfono. Lo único que se persiste es este mapeo a nivel de tenant.
/// </summary>
public class LudoTenantMap
{
    public Guid Id { get; set; }

    /// <summary>ID del tenant en Ludo. Clave de idempotencia (índice único).</summary>
    public string LudoTenantId { get; set; } = string.Empty;

    /// <summary>Tenant correspondiente en TalkIA.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Vertical de negocio: seguro | restaurante | inmobiliario.</summary>
    public string? TipoNegocio { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
