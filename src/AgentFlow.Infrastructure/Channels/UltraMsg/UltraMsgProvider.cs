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
        var doc  = JsonDocument.Parse(json);
        var id   = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        return new SendResult(true, id);
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
        var doc  = JsonDocument.Parse(json);
        var id   = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        return new SendResult(true, id);
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
        var doc  = JsonDocument.Parse(json);
        var id   = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        return new SendResult(true, id);
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
