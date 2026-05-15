using System.Text;
using AgentFlow.Domain.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AgentFlow.Infrastructure.Documents;

/// <summary>
/// Implementación de <see cref="IPdfTextExtractor"/> usando PdfPig (Apache 2.0).
/// Extrae texto por página respetando líneas y espacios; no hace OCR para PDFs
/// puramente escaneados — esos casos los marca el DocumentIndexer como
/// <c>IndexingError = "PDF sin texto extraíble (¿escaneado?)"</c>.
/// </summary>
public class PdfPigTextExtractor : IPdfTextExtractor
{
    public IReadOnlyList<PdfPageText> Extract(byte[] pdfBytes)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
            return [];

        var pages = new List<PdfPageText>();
        using var ms = new MemoryStream(pdfBytes);
        using var doc = PdfDocument.Open(ms);

        foreach (var page in doc.GetPages())
        {
            // ExtractText() preserva orden de lectura típico (top-to-bottom,
            // left-to-right) y respeta los espacios entre words. Suficiente
            // para texto corrido. Tablas anchas se aplanan a una sola línea
            // pero el chunker las parte por longitud — funciona aceptable
            // para retrieval semántico.
            var text = page.Text ?? string.Empty;

            // Normalizar saltos de línea y compactar espacios múltiples.
            // PDFs a veces tienen espacios non-breaking o tabs raros que
            // confunden el chunker. Sin perder estructura de párrafos
            // (dejamos los \n dobles que indican fin de párrafo).
            text = NormalizeWhitespace(text);

            pages.Add(new PdfPageText(page.Number, text));
        }
        return pages;
    }

    private static string NormalizeWhitespace(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var sb = new StringBuilder(raw.Length);
        var lastWasNewline = false;
        var consecutiveNewlines = 0;

        foreach (var ch in raw)
        {
            if (ch == '\r') continue; // ignorar CR (Windows)
            if (ch == '\n')
            {
                consecutiveNewlines++;
                if (consecutiveNewlines <= 2) sb.Append('\n');
                lastWasNewline = true;
                continue;
            }
            // Reemplazar tabs y NBSP por espacio normal.
            var c = (ch == '\t' || ch == ' ') ? ' ' : ch;
            // Colapsar espacios consecutivos.
            if (c == ' ' && sb.Length > 0 && sb[^1] == ' ' && !lastWasNewline) continue;
            sb.Append(c);
            consecutiveNewlines = 0;
            lastWasNewline = false;
        }
        return sb.ToString().Trim();
    }
}
