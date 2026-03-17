namespace AgentFlow.Domain.Entities;

public class AgentDocument
{
    public Guid Id { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public AgentDefinition AgentDefinition { get; set; } = null!;
}
