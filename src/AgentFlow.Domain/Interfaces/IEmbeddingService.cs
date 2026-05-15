namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Genera embeddings vectoriales de texto para el sistema RAG. El embedding es
/// un vector denso de floats (1536 dimensiones para OpenAI text-embedding-3-small)
/// que captura la semántica del texto y permite búsqueda por similitud coseno.
///
/// Se usa en dos lugares:
///   1. Al indexar un PDF: cada chunk se embed-ea y se persiste en BD.
///   2. Al recibir un mensaje del cliente: se embed-ea la pregunta para buscar
///      los chunks más similares vía cosine similarity.
///
/// La implementación productiva es <c>OpenAIEmbeddingService</c> (HTTP a
/// /v1/embeddings). Para tests se puede mockear con embeddings deterministas.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Genera el embedding de un único texto. Para volúmenes (indexado de
    /// muchos chunks) preferir <see cref="GenerateBatchAsync"/> que aprovecha
    /// el batching del provider y reduce llamadas HTTP.
    /// </summary>
    Task<float[]> GenerateAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Genera embeddings para múltiples textos en UNA sola llamada al provider.
    /// OpenAI acepta hasta ~2048 textos por request. La implementación debe
    /// hacer batching interno respetando los límites del provider.
    /// </summary>
    /// <returns>
    /// Lista de float[] en el MISMO orden de entrada. Si algún texto falló
    /// individualmente, la implementación debe levantar excepción (no devolver
    /// arrays parciales) para no corromper el índice.
    /// </returns>
    Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Cantidad de dimensiones que devuelve el modelo configurado. Útil para
    /// validar consistencia al deserializar embeddings persistidos. Para
    /// text-embedding-3-small es 1536.
    /// </summary>
    int Dimensions { get; }
}
