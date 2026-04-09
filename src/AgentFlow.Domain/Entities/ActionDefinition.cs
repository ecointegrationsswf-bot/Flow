namespace AgentFlow.Domain.Entities;

public class ActionDefinition
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty; // SEND_MESSAGE, SEND_RESUME, TRANSFER_CHAT, PREMIUM, etc.
    public string? Description { get; set; }
    public bool RequiresWebhook { get; set; }
    public bool SendsEmail { get; set; }
    public bool SendsSms { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookMethod { get; set; } // GET, POST, PUT

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
