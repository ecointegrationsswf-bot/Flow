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

    // ── RAG: indexación de chunks + embeddings ──────────────────────────
    // Cuando se sube un PDF, un job asíncrono extrae el texto, lo parte en chunks
    // (~500 tokens c/u), genera embeddings vía OpenAI text-embedding-3-small y los
    // persiste en CampaignTemplateDocumentChunks. Ese índice permite que el agente
    // recupere SOLO los fragmentos relevantes por mensaje en vez de inyectar el PDF
    // entero — bajando el costo por turno ~95% y soportando muchos PDFs por maestro.

    /// <summary>
    /// Cuándo se completó la indexación RAG del PDF. NULL = pendiente o falló.
    /// El retriever solo considera documentos con IndexedAt no nulo.
    /// </summary>
    public DateTime? IndexedAt { get; set; }

    /// <summary>
    /// Total de tokens (estimados) que se indexaron desde este PDF. Sirve para
    /// auditar costo del indexado y detectar PDFs anormalmente grandes.
    /// </summary>
    public int? IndexedTokenCount { get; set; }

    /// <summary>
    /// Última excepción durante el indexado, si la última corrida falló. Truncado.
    /// El usuario puede reintentar manualmente desde la UI del admin.
    /// </summary>
    public string? IndexingError { get; set; }

    public CampaignTemplate CampaignTemplate { get; set; } = null!;

    /// <summary>Chunks de texto + embedding generados al indexar el PDF.</summary>
    public ICollection<CampaignTemplateDocumentChunk> Chunks { get; set; } = [];
}
