namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Parte texto extraído de un PDF en fragmentos (chunks) aptos para embedding.
/// El balance entre tamaño de chunk y calidad de retrieval es delicado:
///   - Muy pequeño (&lt;200 tokens): el embedding pierde contexto semántico.
///   - Muy grande (>1000 tokens): un chunk relevante "contamina" la respuesta
///     con detalle irrelevante.
/// El sweet spot empírico para insurance/legal docs es 400-700 tokens, con
/// overlap de 50-100 tokens entre chunks consecutivos para preservar contexto
/// que cruza el límite.
///
/// Implementación: <c>SentenceAwareChunker</c> — respeta párrafos y oraciones,
/// no corta a mitad de palabra.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Parte las páginas extraídas en chunks. Cada chunk conserva el PageNumber
    /// de la página de origen (para citas al cliente). Si un chunk cruza páginas
    /// (cuando un párrafo se extiende a la siguiente), se queda con la página
    /// donde inició.
    /// </summary>
    IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PdfPageText> pages);
}

/// <summary>Un fragmento de texto listo para embed.</summary>
/// <param name="PageNumber">Página del PDF donde inicia este chunk.</param>
/// <param name="ChunkIndex">Orden global dentro del documento (0..N).</param>
/// <param name="Text">Texto plano del chunk. Idealmente 400-700 tokens.</param>
/// <param name="TokenCount">Estimación de tokens (chars/4) — sanity check.</param>
public record TextChunk(int PageNumber, int ChunkIndex, string Text, int TokenCount);
