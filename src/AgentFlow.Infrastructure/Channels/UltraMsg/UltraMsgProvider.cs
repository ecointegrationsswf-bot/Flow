using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.Channels.UltraMsg;

public class UltraMsgProvider(HttpClient http, UltraMsgOptions options) : IChannelProvider
{
    public ChannelType ChannelType => ChannelType.WhatsApp;
    public ProviderType ProviderType => ProviderType.UltraMsg;

    public async Task<SendResult> SendMessageAsync(SendMessageRequest req, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(req.MediaUrl))
        {
            return req.MediaType switch
            {
                "image"    => await SendImageAsync(req, ct),
                "document" => await SendDocumentAsync(req, ct),
                _          => await SendTextAsync(req, ct)
            };
        }
        return await SendTextAsync(req, ct);
    }

    private async Task<SendResult> SendTextAsync(SendMessageRequest req, CancellationToken ct)
    {
        var payload = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = options.Token,
            ["to"]    = req.To,
            ["body"]  = req.Body
        });
        var response = await http.PostAsync(
            $"https://api.ultramsg.com/{NormalizeInstanceId(options.InstanceId)}/messages/chat", payload, ct);

        if (!response.IsSuccessStatusCode)
            return new SendResult(false, null, $"UltraMsg HTTP {response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(ct);
        return InterpretUltraMsgResponse(json, "chat");
    }

    private async Task<SendResult> SendImageAsync(SendMessageRequest req, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["token"]   = options.Token,
            ["to"]      = req.To,
            ["image"]   = req.MediaUrl!,
            ["caption"] = req.Body
        };
        var response = await http.PostAsync(
            $"https://api.ultramsg.com/{NormalizeInstanceId(options.InstanceId)}/messages/image",
            new FormUrlEncodedContent(fields), ct);

        if (!response.IsSuccessStatusCode)
            return new SendResult(false, null, $"UltraMsg image HTTP {response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(ct);
        return InterpretUltraMsgResponse(json, "image");
    }

    private async Task<SendResult> SendDocumentAsync(SendMessageRequest req, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["token"]    = options.Token,
            ["to"]       = req.To,
            ["document"] = req.MediaUrl!,
            ["filename"] = req.Filename ?? "documento.pdf",
            ["caption"]  = req.Body
        };
        var response = await http.PostAsync(
            $"https://api.ultramsg.com/{NormalizeInstanceId(options.InstanceId)}/messages/document",
            new FormUrlEncodedContent(fields), ct);

        if (!response.IsSuccessStatusCode)
            return new SendResult(false, null, $"UltraMsg document HTTP {response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(ct);
        return InterpretUltraMsgResponse(json, "document");
    }

    /// <summary>
    /// Interpreta el body de UltraMsg para distinguir éxito REAL de aceptación
    /// pasiva (que UltraMsg encola internamente cuando la línea está desconectada).
    ///
    /// Caso que motivó este check (Somos Seguros 18/05): la línea cayó a mitad
    /// de la campaña, UltraMsg siguió respondiendo HTTP 200 con `{"sent":"false"}`
    /// para cada request. Nuestro código antiguo solo miraba HTTP code → todo
    /// pasaba como éxito → UltraMsg encoló 95 mensajes → al reconectar, ráfaga
    /// → Meta bloqueó el número.
    ///
    /// Casos de respuesta documentados:
    ///   ✅ {"sent":"true","message":"ok","id":"123@c.us-456789@g.us"}
    ///   ✅ {"sent":true,"id":"..."}                    (sent como bool)
    ///   🚫 {"sent":"false","message":"WhatsApp not connected"}
    ///   🚫 {"sent":"queued","id":"..."}                (línea down, encolado interno)
    ///   🚫 {"error":"WhatsApp not connected"}
    ///   🚫 {"error":"Wrong token"}
    ///   🚫 {} o body vacío sin id
    ///
    /// Devuelve Success solo cuando `sent="true"` (case-insensitive) Y hay `id` no vacío.
    /// </summary>
    internal static SendResult InterpretUltraMsgResponse(string json, string opLabel)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SendResult(false, null, $"UltraMsg {opLabel}: respuesta vacía (línea posiblemente caída)");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex)
        {
            return new SendResult(false, null, $"UltraMsg {opLabel}: JSON inválido ({ex.Message})");
        }

        using (doc)
        {
            var root = doc.RootElement;

            // 1. Rechazar cualquier `error` en el body (independiente del HTTP code).
            if (root.TryGetProperty("error", out var errProp))
            {
                var errMsg = errProp.ValueKind == JsonValueKind.String ? errProp.GetString() : errProp.ToString();
                return new SendResult(false, null, $"UltraMsg {opLabel} error: {errMsg}");
            }

            // 2. Leer `sent` (string "true"/"false"/"queued" o bool).
            string? sentValue = null;
            if (root.TryGetProperty("sent", out var sentProp))
            {
                sentValue = sentProp.ValueKind switch
                {
                    JsonValueKind.String => sentProp.GetString()?.ToLowerInvariant(),
                    JsonValueKind.True   => "true",
                    JsonValueKind.False  => "false",
                    _                    => sentProp.ToString().ToLowerInvariant()
                };
            }

            // 3. Si `sent` existe y NO es "true" → falla. "queued" / "false" / cualquier
            //    otra cosa significa que UltraMsg aceptó la request pero NO entregó
            //    (típico cuando la línea está desconectada — encola internamente).
            if (sentValue is not null && !string.Equals(sentValue, "true", StringComparison.OrdinalIgnoreCase))
            {
                var detail = root.TryGetProperty("message", out var msgProp)
                    ? (msgProp.ValueKind == JsonValueKind.String ? msgProp.GetString() : msgProp.ToString())
                    : null;
                return new SendResult(false, null,
                    $"UltraMsg {opLabel} no entregado (sent={sentValue}): {detail ?? "línea posiblemente desconectada"}");
            }

            // 4. Si el body NO tiene `sent` (formato legacy o respuesta incompleta),
            //    exigimos al menos un `id` válido para considerarlo éxito.
            string? id = null;
            if (root.TryGetProperty("id", out var idProp))
            {
                id = idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : idProp.ToString();
            }

            if (string.IsNullOrWhiteSpace(id))
                return new SendResult(false, null,
                    $"UltraMsg {opLabel}: respuesta sin id ni sent=true — no se puede confirmar envío");

            return new SendResult(true, id);
        }
    }

    public Task<MessageStatusResult> GetMessageStatusAsync(string externalMessageId, CancellationToken ct = default)
        => Task.FromResult(new MessageStatusResult(externalMessageId, "sent"));

    public bool ValidateWebhookSignature(string payload, string signature)
    {
        // UltraMsg envía token en el body; validación básica
        return payload.Contains(options.Token);
    }

    // UltraMsg usa el prefijo "instance" en la URL (ej: instance140984)
    // pero a veces se almacena solo el número (140984) — normalizar
    private static string NormalizeInstanceId(string id)
        => id.StartsWith("instance", StringComparison.OrdinalIgnoreCase) ? id : $"instance{id}";
}

public record UltraMsgOptions(string InstanceId, string Token);
