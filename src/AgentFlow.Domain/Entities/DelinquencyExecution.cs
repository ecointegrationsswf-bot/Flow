using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Registra una ejecución del job de descarga de morosidad para una acción concreta.
/// Un registro por disparo del ScheduledWebhookJob asociado a esa acción.
/// </summary>
public class DelinquencyExecution
{
    public Guid Id { get; set; }

    public Guid ActionDefinitionId { get; set; }
    public ActionDefinition? ActionDefinition { get; set; }

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Job programado que disparó esta ejecución. Null si fue invocado manualmente.</summary>
    public Guid? ScheduledWebhookJobId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public DelinquencyExecutionStatus Status { get; set; } = DelinquencyExecutionStatus.Pending;

    // Métricas de la ejecución
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int DiscardedItems { get; set; }
    public int GroupsCreated { get; set; }
    public int CampaignsCreated { get; set; }

    /// <summary>Mensaje de error si Status = Failed o PartiallyFailed.</summary>
    public string? ErrorMessage { get; set; }

    public ICollection<DelinquencyItem> Items { get; set; } = [];
    public ICollection<ContactGroup> Groups { get; set; } = [];
}
