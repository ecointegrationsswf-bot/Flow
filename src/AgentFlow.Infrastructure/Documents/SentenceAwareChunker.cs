using System.Text;
using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.Documents;

/// <summary>
/// Parte texto en chunks respetando límites semánticos (párrafos → oraciones →
/// palabras). Mantiene los chunks entre <see cref="TargetTokens"/> y
/// <see cref="MaxTokens"/> con un overlap configurable entre consecutivos para
/// preservar contexto que cruza fronteras.
///
/// Heurística de tokens: <c>chars / 4</c>. No es exacto pero es lo suficiente
/// para el chunking (no estamos cobrando por estos tokens; el costo real lo paga
/// OpenAI sobre tokenización BPE precisa).
///
/// Estrategia:
///   1. Por cada página, partir en párrafos (split por \n\n).
///   2. Acumular párrafos hasta llegar al tamaño objetivo.
///   3. Si un párrafo solo excede el max, partirlo en oraciones (split por .!?).
///   4. Si una oración sola excede el max, partirla por longitud bruta.
///   5. Entre chunk y chunk: arrastrar ~OverlapTokens del final del anterior al inicio del siguiente.
/// </summary>
public class SentenceAwareChunker : ITextChunker
{
    private const int TargetTokens = 500;
    private const int MaxTokens    = 700;
    private const int MinTokens    = 100;   // chunks muy chicos no se emitten (se mergean con el siguiente)
    private const int OverlapTokens = 60;
    private const int CharsPerToken = 4;     // heurística rough

    private static readonly char[] SentenceTerminators = ['.', '!', '?', ';'];

    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PdfPageText> pages)
    {
        if (pages is null || pages.Count == 0) return [];

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text)) continue;

            // Split por párrafos (\n\n). Si no hay dobles \n, todo es un párrafo.
            var paragraphs = page.Text
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Replace('\n', ' ').Trim())
                .Where(p => p.Length > 0)
                .ToList();

            var buffer = new StringBuilder();
            string? overlap = null;

            foreach (var para in paragraphs)
            {
                var paragraphTokens = EstimateTokens(para);

                if (paragraphTokens > MaxTokens)
                {
                    // Párrafo gigante — flush lo que hay y partir el párrafo por oraciones.
                    Flush(buffer, page.PageNumber, ref chunkIndex, chunks, ref overlap);
                    foreach (var sentenceChunk in SplitLongParagraph(para))
                    {
                        // Cada sub-chunk del párrafo se emite directo (ya viene dimensionado).
                        EmitChunk(sentenceChunk, page.PageNumber, ref chunkIndex, chunks, ref overlap);
                    }
                    continue;
                }

                // Si agregar este párrafo excede el max, flush primero.
                var pending = EstimateTokens(buffer.ToString());
                if (pending + paragraphTokens > MaxTokens && pending >= MinTokens)
                {
                    Flush(buffer, page.PageNumber, ref chunkIndex, chunks, ref overlap);
                }

                if (buffer.Length > 0) buffer.Append("\n\n");
                buffer.Append(para);

                // Si llegamos al target, flush (no esperamos al max).
                if (EstimateTokens(buffer.ToString()) >= TargetTokens)
                {
                    Flush(buffer, page.PageNumber, ref chunkIndex, chunks, ref overlap);
                }
            }
            // Al cambiar de página flusheamos lo pendiente (mantenemos la cita por página).
            Flush(buffer, page.PageNumber, ref chunkIndex, chunks, ref overlap);
        }

        return chunks;
    }

    private static void Flush(
        StringBuilder buffer, int pageNumber, ref int chunkIndex,
        List<TextChunk> chunks, ref string? overlap)
    {
        var text = buffer.ToString().Trim();
        buffer.Clear();
        if (text.Length == 0) return;

        EmitChunk(text, pageNumber, ref chunkIndex, chunks, ref overlap);
    }

    private static void EmitChunk(
        string text, int pageNumber, ref int chunkIndex,
        List<TextChunk> chunks, ref string? overlap)
    {
        // Aplicar el overlap del chunk anterior al inicio de este (para
        // preservar contexto). Si el overlap repite literalmente el inicio
        // del nuevo texto, no lo agregamos (evita duplicación visual).
        if (overlap is not null && !text.StartsWith(overlap, StringComparison.Ordinal))
        {
            text = overlap + "\n\n" + text;
        }

        var tokens = EstimateTokens(text);
        chunks.Add(new TextChunk(pageNumber, chunkIndex++, text, tokens));

        // Calcular overlap: tomar los últimos N tokens del chunk emitido.
        overlap = TakeTail(text, OverlapTokens);
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        // Partir el párrafo gigante por oraciones; acumular hasta cerca del target.
        var sentences = SplitIntoSentences(paragraph);
        var buffer = new StringBuilder();
        foreach (var sentence in sentences)
        {
            var sentTokens = EstimateTokens(sentence);
            if (sentTokens > MaxTokens)
            {
                // Oración monstruosa (sin puntuación): flush y partir bruto.
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                }
                foreach (var hardSlice in HardSplit(sentence, MaxTokens))
                    yield return hardSlice;
                continue;
            }

            var pending = EstimateTokens(buffer.ToString());
            if (pending + sentTokens > MaxTokens && pending >= MinTokens)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
            }
            if (buffer.Length > 0) buffer.Append(' ');
            buffer.Append(sentence);

            if (EstimateTokens(buffer.ToString()) >= TargetTokens)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
            }
        }
        if (buffer.Length > 0) yield return buffer.ToString().Trim();
    }

    private static IEnumerable<string> SplitIntoSentences(string paragraph)
    {
        // Simple split por terminadores; mantenemos la puntuación pegada a la oración.
        var sb = new StringBuilder();
        foreach (var ch in paragraph)
        {
            sb.Append(ch);
            if (SentenceTerminators.Contains(ch))
            {
                var sentence = sb.ToString().Trim();
                if (sentence.Length > 0) yield return sentence;
                sb.Clear();
            }
        }
        var tail = sb.ToString().Trim();
        if (tail.Length > 0) yield return tail;
    }

    private static IEnumerable<string> HardSplit(string text, int maxTokens)
    {
        var maxChars = maxTokens * CharsPerToken;
        for (var i = 0; i < text.Length; i += maxChars)
            yield return text.Substring(i, Math.Min(maxChars, text.Length - i));
    }

    private static string TakeTail(string text, int tokens)
    {
        var chars = tokens * CharsPerToken;
        if (text.Length <= chars) return text;

        // Intentar cortar en un espacio para no romper una palabra.
        var startIdx = text.Length - chars;
        var space = text.IndexOf(' ', startIdx);
        if (space > 0 && space - startIdx < CharsPerToken * 20) startIdx = space + 1;
        return text[startIdx..].Trim();
    }

    private static int EstimateTokens(string text)
        => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / CharsPerToken);
}
