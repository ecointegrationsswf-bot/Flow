using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;


/// <summary>
/// Campaña de comunicación. Puede iniciarse por archivo cargado
/// o por evento automático (póliza vencida, morosidad detectada).
/// </summary>
public class Campaign
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid AgentDefinitionId { get; set; }
    public AgentDefinition AgentDefinition { get; set; } = null!;

    // Maestro de campaña (opcional para campañas legacy)
    public Guid? CampaignTemplateId { get; set; }
    public CampaignTemplate? CampaignTemplate { get; set; }

    public string Name { get; set; } = string.Empty;
    public CampaignTrigger Trigger { get; set; }
    public ChannelType Channel { get; set; }
    public bool IsActive { get; set; } = true;

    // Archivo (cuando Trigger = FileUpload)
    public string? SourceFileName { get; set; }
    public string? SourceFilePath { get; set; }
    public int TotalContacts { get; set; }
    public int ProcessedContacts { get; set; }

    // Programación
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = string.Empty;

    // Lanzamiento
    public CampaignStatus Status { get; set; } = CampaignStatus.Pending;
    public DateTime? LaunchedAt { get; set; }
    public string? LaunchedByUserId { get; set; }

    /// <summary>
    /// Teléfono WhatsApp del ejecutivo que lanzó la campaña (copiado desde AppUser.NotifyPhone
    /// al momento del lanzamiento). Se usa para el TRANSFER_CHAT — notificar al ejecutivo
    /// cuando un cliente solicita atención humana.
    /// </summary>
    public string? LaunchedByUserPhone { get; set; }

    public ICollection<CampaignContact> Contacts { get; set; } = [];
}
