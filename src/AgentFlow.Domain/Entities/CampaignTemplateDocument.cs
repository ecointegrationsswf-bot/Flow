namespace AgentFlow.Domain.Entities;

/// <summary>
/// Documento de referencia (PDF) asociado a un maestro de campaña.
/// Los PDFs se persisten en Azure Blob Storage y el agente los usa como
/// contexto al responder mensajes de la campaña.
/// </summary>
public class CampaignTemplateDocument
{
    public Guid Id { get; set; }
    public Guid CampaignTemplateId { get; set; }
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Descripción opcional para orientar al agente cuándo consultar este documento.
    /// Se incluye en el bloque "DOCUMENTOS DE REFERENCIA" del system prompt.
    /// Ej: "Cobertura de accidentes personales del plan 2026".
    /// </summary>
    public string? Description { get; set; }

    public CampaignTemplate CampaignTemplate { get; set; } = null!;
}
