using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Representa una linea/numero de WhatsApp asociada a un tenant.
/// Un tenant puede tener multiples lineas, cada una con su propia instancia UltraMsg.
/// </summary>
public class WhatsAppLine
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }

    public string DisplayName { get; set; } = string.Empty;       // ej: "Cobros Principal"
    public string PhoneNumber { get; set; } = string.Empty;       // E.164 — se actualiza al vincular
    public string InstanceId { get; set; } = string.Empty;        // UltraMsg instance ID | Meta phone_number_id
    public string ApiToken { get; set; } = string.Empty;          // UltraMsg token (vacío en líneas Meta)
    public ProviderType Provider { get; set; } = ProviderType.UltraMsg;
    public bool IsActive { get; set; } = true;

    // ── Credenciales Meta WhatsApp Cloud API (solo si Provider == MetaCloudApi) ──
    // Aditivo: las líneas UltraMsg dejan estos campos en null. El phone_number_id
    // se reutiliza en InstanceId (mantiene el índice único y la resolución del webhook).
    // NOTA: se guardan en texto plano igual que el token UltraMsg actual; cifrado
    // en reposo queda como hardening pendiente (aplica a ambos proveedores).
    public string? MetaWabaId { get; set; }          // WhatsApp Business Account ID (plantillas/webhook)
    public string? MetaAccessToken { get; set; }     // Bearer del Graph API
    public string? MetaAppSecret { get; set; }        // valida la firma HMAC del webhook entrante
    public string? MetaBusinessId { get; set; }       // portfolio / business_id (verificación)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Monitor diario de salud (job WHATSAPP_LINE_HEALTH_CHECK a las 6am Panamá).
    // LastStatus: "authenticated" | "disconnected" | "qr" | "loading" | "unknown".
    // Una línea se considera caída cuando LastStatus != "authenticated"
    // Y ConsecutivePingFailures >= 2 (tolerancia a flake puntual de red).
    public string? LastStatus { get; set; }
    public DateTime? LastStatusCheckedAt { get; set; }
    public int ConsecutivePingFailures { get; set; }

    public Tenant? Tenant { get; set; }
}
