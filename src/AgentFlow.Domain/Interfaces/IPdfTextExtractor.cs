namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Extrae texto de un PDF página por página. Es el primer paso del pipeline
/// de indexado RAG — luego el texto se parte en chunks y se embed-ea.
///
/// Implementación productiva: <c>PdfPigTextExtractor</c> (PdfPig, Apache 2.0).
///
/// Limitaciones conocidas:
///   - PDFs escaneados (puro raster, sin texto embed) devuelven páginas vacías;
///     hoy no hacemos OCR. El indexer marca IndexingError si todas las páginas
///     vienen vacías para que el admin pueda revisarlo.
///   - Tablas complejas se aplanan a texto plano — la calidad del chunk puede
///     bajar para tablas anchas, pero la consulta semántica las rescata.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extrae el texto del PDF (bytes en memoria) por página.
    /// </summary>
    /// <param name="pdfBytes">Contenido completo del PDF.</param>
    /// <returns>Lista de páginas con su texto extraído, en orden.</returns>
    IReadOnlyList<PdfPageText> Extract(byte[] pdfBytes);
}

/// <summary>Texto extraído de UNA página del PDF.</summary>
/// <param name="PageNumber">Número de página, 1-indexed.</param>
/// <param name="Text">Texto plano de la página. Puede ser vacío.</param>
public record PdfPageText(int PageNumber, string Text);
