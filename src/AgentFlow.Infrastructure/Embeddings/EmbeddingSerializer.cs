using System.Buffers.Binary;

namespace AgentFlow.Infrastructure.Embeddings;

/// <summary>
/// Serializa/deserializa float[] (embeddings) a byte[] compacto para persistir
/// en VARBINARY(MAX). Formato: little-endian IEEE 754 (4 bytes por float).
///
/// Para text-embedding-3-small: 1536 floats × 4 bytes = 6144 bytes por chunk.
/// Más compacto que JSON ("[0.123,0.456,...]" ≈ 15-20k chars para el mismo vector).
///
/// IMPORTANTE: el orden little-endian es fijo para portabilidad entre máquinas
/// (Windows x64 es little-endian; explícito por si en el futuro Worker corre
/// en otra arquitectura).
/// </summary>
public static class EmbeddingSerializer
{
    public static byte[] ToBytes(float[] embedding)
    {
        if (embedding is null) throw new ArgumentNullException(nameof(embedding));
        var bytes = new byte[embedding.Length * sizeof(float)];
        var span = bytes.AsSpan();
        for (var i = 0; i < embedding.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(i * 4, 4), embedding[i]);
        }
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length % sizeof(float) != 0)
            throw new ArgumentException(
                $"Buffer length {bytes.Length} is not a multiple of {sizeof(float)} — corrupto.",
                nameof(bytes));

        var result = new float[bytes.Length / sizeof(float)];
        var span = bytes.AsSpan();
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(i * 4, 4));
        }
        return result;
    }

    /// <summary>
    /// Cosine similarity entre dos vectores. Asume ambos están normalizados o
    /// no — la fórmula completa funciona en ambos casos. OpenAI text-embedding-3
    /// devuelve vectores YA normalizados (norm=1), así que el denominador podría
    /// omitirse — lo mantenemos por seguridad si en el futuro se usa otro modelo.
    /// </summary>
    /// <returns>Valor entre -1 (opuesto) y 1 (idéntico). Típico: 0.3-0.8.</returns>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException(
                $"Dimension mismatch: {a.Length} vs {b.Length}. ¿Cambió el modelo de embedding?");

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
