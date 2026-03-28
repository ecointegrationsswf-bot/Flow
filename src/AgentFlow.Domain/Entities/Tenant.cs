using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Representa un cliente/corredor que usa la plataforma.
/// Cada tenant tiene su propio número de WhatsApp y configuración de proveedor.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;           // identificador URL-safe
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Canal WhatsApp
    public ProviderType WhatsAppProvider { get; set; }
    public string WhatsAppPhoneNumber { get; set; } = string.Empty;
    public string WhatsAppApiToken { get; set; } = string.Empty;  // cifrado en BD
    public string? WhatsAppInstanceId { get; set; }               // UltraMsg instanceId

    // Facturación y país
    public string Country { get; set; } = string.Empty;
    public decimal MonthlyBillingAmount { get; set; }

    // Configuración horaria
    public TimeOnly BusinessHoursStart { get; set; } = new(8, 0);
    public TimeOnly BusinessHoursEnd { get; set; }   = new(17, 0);
    public string TimeZone { get; set; } = "America/Panama";

    // Configuración LLM — cada tenant elige su proveedor de IA y su API key
    public LlmProviderType LlmProvider { get; set; } = LlmProviderType.Anthropic;
    public string? LlmApiKey { get; set; }            // cifrado en BD, nunca se expone completo
    public string LlmModel { get; set; } = "claude-sonnet-4-6";  // modelo por defecto

    // Configuración SendGrid para envío de emails
    public string? SendGridApiKey { get; set; }
    public string? SenderEmail { get; set; }

    public ICollection<AgentDefinition> Agents { get; set; } = [];
    public ICollection<Campaign> Campaigns { get; set; } = [];
    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<WhatsAppLine> WhatsAppLines { get; set; } = [];
}
