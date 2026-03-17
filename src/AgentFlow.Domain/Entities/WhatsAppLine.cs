using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Representa una linea/numero de WhatsApp asociada a un tenant.
/// Un tenant puede tener multiples lineas, cada una con su propia instancia UltraMsg.
/// </summary>
public class WhatsAppLine
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string DisplayName { get; set; } = string.Empty;       // ej: "Cobros Principal"
    public string PhoneNumber { get; set; } = string.Empty;       // E.164 — se actualiza al vincular
    public string InstanceId { get; set; } = string.Empty;        // UltraMsg instance ID
    public string ApiToken { get; set; } = string.Empty;          // UltraMsg token
    public ProviderType Provider { get; set; } = ProviderType.UltraMsg;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
