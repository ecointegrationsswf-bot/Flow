using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Agrupa todos los ítems de morosidad que comparten el mismo teléfono normalizado.
/// Un ContactGroup = un contacto único en la campaña de WhatsApp que se generará.
/// </summary>
public class ContactGroup
{
    public Guid Id { get; set; }

    public Guid ExecutionId { get; set; }
    public DelinquencyExecution? Execution { get; set; }

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Teléfono E.164 — clave de agrupación. Ej: "+50766123456".</summary>
    public string PhoneNormalized { get; set; } = string.Empty;

    /// <summary>Nombre del cliente, tomado del primer ítem del grupo con nombre no vacío.</summary>
    public string? ClientName { get; set; }

    /// <summary>Suma de Amount de todos los ítems válidos del grupo.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Cantidad de pólizas/registros agrupados para este teléfono.</summary>
    public int ItemCount { get; set; }

    public ContactGroupStatus Status { get; set; } = ContactGroupStatus.Pending;

    /// <summary>Campaña de WhatsApp creada para este grupo. Null hasta que AutoCrearCampanas lo genere.</summary>
    public Guid? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC. Cuándo n8n confirmó que el primer WhatsApp salió a este teléfono. NULL = aún no enviado.</summary>
    public DateTime? FirstMessageSentAt { get; set; }

    /// <summary>UTC. Primer inbound del cliente tras la campaña. NULL = no ha respondido.</summary>
    public DateTime? FirstClientReplyAt { get; set; }

    public ICollection<DelinquencyItem> Items { get; set; } = [];
}
