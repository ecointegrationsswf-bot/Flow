using AgentFlow.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;

namespace AgentFlow.Infrastructure.AI;

/// <summary>
/// Transcribe notas de voz de WhatsApp usando OpenAI Whisper API.
/// Soporta OGG/Opus (formato nativo de WhatsApp PTT), MP3, WAV, M4A.
/// Configuración: OpenAI:ApiKey en appsettings.
/// </summary>
public class WhisperTranscriptionService(
    System.Net.Http.IHttpClientFactory httpClientFactory,
    IConfiguration cfg
) : ITranscriptionService
{
    private readonly string? _apiKey = cfg["OpenAI:ApiKey"];

    public async Task<string?> TranscribeAsync(byte[] audioBytes, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey) || _apiKey == "YOUR_OPENAI_API_KEY")
        {
            Console.WriteLine("[Whisper] OpenAI API key no configurada — se omite transcripción.");
            return null;
        }

        if (audioBytes.Length == 0)
        {
            Console.WriteLine("[Whisper] Audio vacío — se omite transcripción.");
            return null;
        }

        try
        {
            var http = httpClientFactory.CreateClient();

            // Determinar content-type según extensión
            var ext = Path.GetExtension(fileName).ToLower().TrimStart('.');
            var contentType = ext switch
            {
                "ogg"  => "audio/ogg",
                "mp3"  => "audio/mpeg",
                "m4a"  => "audio/mp4",
                "wav"  => "audio/wav",
                "webm" => "audio/webm",
                _      => "audio/ogg"
            };

            using var form = new MultipartFormDataContent();

            var audioContent = new ByteArrayContent(audioBytes);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(audioContent, "file", fileName);
            form.Add(new StringContent("whisper-1"), "model");
            form.Add(new StringContent("es"), "language");  // español — mejora precisión
            form.Add(new StringContent("text"), "response_format");

            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.openai.com/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = form;

            var response = await http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Whisper] Error HTTP {(int)response.StatusCode}: {responseBody}");
                return null;
            }

            // response_format=text devuelve el texto plano directamente
            var transcription = responseBody.Trim();
            Console.WriteLine($"[Whisper] Transcripción exitosa ({audioBytes.Length / 1024}KB): \"{transcription}\"");
            return string.IsNullOrWhiteSpace(transcription) ? null : transcription;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Whisper] Error al transcribir: {ex.Message}");
            return null;
        }
    }
}
