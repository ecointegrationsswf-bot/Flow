using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AgentFlow.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Embeddings;

/// <summary>
/// Implementación de <see cref="IEmbeddingService"/> contra la API REST de OpenAI
/// (/v1/embeddings). La API key es GLOBAL — se lee de <c>OpenAI:ApiKey</c> de
/// IConfiguration (la misma que usa WhisperTranscriptionService). NO se acepta
/// key por tenant: una sola cuenta OpenAI sirve a todos los corredores.
///
/// Modelo configurable vía <c>OpenAI:EmbeddingModel</c>; default
/// <c>text-embedding-3-small</c> (1536 dim, $0.02/1M tokens). Para subir a
/// <c>text-embedding-3-large</c> (3072 dim, $0.13/1M) habría que reindexar
/// TODOS los chunks porque las dimensiones cambian — el FromBytes detectaría
/// la inconsistencia.
///
/// Retry: 3 intentos con exponential backoff. OpenAI retorna 429/5xx de manera
/// transitoria con cierta frecuencia bajo carga; sin retry el indexado falla
/// completo. El retrieval (1 sola embed-call por mensaje) tolera el delay sin
/// problema; el indexado batch (60+ chunks) se beneficia mucho.
/// </summary>
public class OpenAIEmbeddingService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<OpenAIEmbeddingService> log) : IEmbeddingService
{
    private const string EmbeddingsEndpoint = "https://api.openai.com/v1/embeddings";
    private const int DefaultDimensions = 1536;       // text-embedding-3-small
    private const int MaxBatchSize = 200;             // OpenAI tope soft

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(8),
    ];

    private string ApiKey =>
        config["OpenAI:ApiKey"]
        ?? throw new InvalidOperationException(
            "OpenAI:ApiKey no configurada. RAG requiere una API key global de OpenAI.");

    private string Model => config["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

    public int Dimensions => DefaultDimensions;

    public async Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
    {
        var batch = await GenerateBatchAsync(new[] { text }, ct);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts is null || texts.Count == 0) return [];

        // Si excede el batch máximo, partimos en sub-lotes secuenciales.
        var results = new List<float[]>(texts.Count);
        for (var offset = 0; offset < texts.Count; offset += MaxBatchSize)
        {
            var slice = texts.Skip(offset).Take(MaxBatchSize).ToList();
            var subResult = await CallOpenAIWithRetryAsync(slice, ct);
            results.AddRange(subResult);
        }
        return results;
    }

    private async Task<List<float[]>> CallOpenAIWithRetryAsync(
        List<string> texts, CancellationToken ct)
    {
        // OpenAI rechaza textos vacíos. Reemplazamos por un placeholder mínimo
        // para mantener la indexación 1:1 — el caller asume orden estable.
        var sanitized = texts
            .Select(t => string.IsNullOrWhiteSpace(t) ? " " : t)
            .ToArray();

        var payload = new
        {
            input = sanitized,
            model = Model,
            encoding_format = "float",
        };

        Exception? lastEx = null;
        for (var attempt = 0; attempt < RetryDelays.Length + 1; attempt++)
        {
            try
            {
                var http = httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(60);
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", ApiKey);

                using var resp = await http.PostAsJsonAsync(EmbeddingsEndpoint, payload, ct);

                if (resp.IsSuccessStatusCode)
                {
                    var parsed = await resp.Content.ReadFromJsonAsync<EmbeddingsResponse>(ct);
                    if (parsed?.Data is null || parsed.Data.Count != texts.Count)
                        throw new InvalidOperationException(
                            $"OpenAI devolvió {parsed?.Data?.Count ?? 0} embeddings; esperaba {texts.Count}.");

                    return parsed.Data
                        .OrderBy(d => d.Index)
                        .Select(d => d.Embedding)
                        .ToList();
                }

                // 429 (rate limit) o 5xx → retry. 4xx restantes → tira inmediato.
                var status = (int)resp.StatusCode;
                var isRetryable = status == 429 || status >= 500;
                var body = await resp.Content.ReadAsStringAsync(ct);
                var preview = body.Length > 400 ? body[..400] : body;

                if (!isRetryable || attempt == RetryDelays.Length)
                {
                    throw new InvalidOperationException(
                        $"OpenAI embeddings retornó HTTP {status}: {preview}");
                }

                log.LogWarning(
                    "[OpenAI Embeddings] HTTP {Status} en intento {Attempt}; reintentando en {Delay}s — preview: {Body}",
                    status, attempt + 1, RetryDelays[attempt].TotalSeconds, preview);
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (HttpRequestException ex) when (attempt < RetryDelays.Length)
            {
                lastEx = ex;
                log.LogWarning(ex,
                    "[OpenAI Embeddings] Error de red en intento {Attempt}; reintentando en {Delay}s",
                    attempt + 1, RetryDelays[attempt].TotalSeconds);
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt < RetryDelays.Length)
            {
                lastEx = ex;
                log.LogWarning(ex,
                    "[OpenAI Embeddings] Timeout en intento {Attempt}; reintentando en {Delay}s",
                    attempt + 1, RetryDelays[attempt].TotalSeconds);
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }

        throw new InvalidOperationException(
            "OpenAI embeddings falló después de todos los reintentos.", lastEx);
    }

    // DTOs internos del response. Ver: https://platform.openai.com/docs/api-reference/embeddings/object
    private sealed class EmbeddingsResponse
    {
        [JsonPropertyName("data")] public List<EmbeddingData> Data { get; set; } = new();
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("index")]     public int Index { get; set; }
        [JsonPropertyName("embedding")] public float[] Embedding { get; set; } = [];
    }
}
