namespace AgentFlow.Domain.Entities;

/// <summary>
/// Fragmento de texto extraído de un <see cref="CampaignTemplateDocument"/> con su
/// embedding vectorial asociado. Es la unidad básica del índice RAG.
///
/// Pipeline de creación (al subir un PDF al maestro):
///   1. Extraer texto del PDF por página (PdfPig)
///   2. Partir el texto en chunks (~500 tokens c/u, overlap 50 entre consecutivos)
///   3. Por cada chunk: pedir embedding a OpenAI (text-embedding-3-small → 1536 dim)
///   4. Persistir esta entidad con Text + Embedding serializado a bytes
///
/// Pipeline de consulta (cada mensaje del cliente):
///   1. Embed la pregunta del cliente
///   2. Cosine similarity de la query contra TODOS los embeddings del tenant
///   3. Top K chunks (default 5) → se inyectan como contexto al system prompt
///
/// El embedding se almacena como float[] serializado a binario little-endian para
/// compactar (1536 floats × 4 bytes = 6144 bytes por chunk). Cosine similarity se
/// calcula en C# memoria — adecuado hasta ~50k chunks por tenant.
/// </summary>
public class CampaignTemplateDocumentChunk
{
    public Guid Id { get; set; }

    /// <summary>FK al documento de origen. Cascade delete: si se borra el PDF, se borran sus chunks.</summary>
    public Guid DocumentId { get; set; }
    public CampaignTemplateDocument Document { get; set; } = null!;

    /// <summary>
    /// Denormalizado del documento padre para filtrar rápido por tenant al hacer
    /// retrieval sin necesidad de JOIN. Mantener consistente con Document.TenantId.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Denormalizado del documento padre. Permite priorizar chunks del maestro
    /// activo al hacer retrieval (mismo patrón que la carga de PDFs enteros hoy).
    /// </summary>
    public Guid CampaignTemplateId { get; set; }

    /// <summary>
    /// Página del PDF de donde proviene este chunk. Se incluye en la cita que el
    /// agente puede mostrar al cliente ("según el documento X, página Y").
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Orden secuencial dentro del documento (0, 1, 2, ...). Útil para debugging
    /// del chunking y para mantener orden estable si se reindexa.
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Texto crudo del chunk — lo que va al system prompt cuando este chunk es
    /// recuperado. Idealmente entre 300 y 700 tokens.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex del Text. Sirve para deduplicar al re-indexar (si el contenido
    /// del chunk no cambió, podemos reutilizar el embedding existente y evitar
    /// llamadas a OpenAI). 64 caracteres hex fijos.
    /// </summary>
    public string TextHash { get; set; } = string.Empty;

    /// <summary>
    /// Embedding del Text serializado como bytes (float[] little-endian).
    /// Para text-embedding-3-small son 1536 floats × 4 bytes = 6144 bytes.
    /// Deserializar con <c>EmbeddingSerializer.FromBytes(bytes)</c>.
    /// </summary>
    public byte[] Embedding { get; set; } = [];

    /// <summary>
    /// Cantidad estimada de tokens del Text (calculado al chunking — heurística
    /// chars/4). Usado para sanity checks y métricas de costo del indexado.
    /// </summary>
    public int TokenCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
