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
        var payload = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = options.Token,
            ["to"]    = req.To,
            ["body"]  = req.Body
        });

        var response = await http.PostAsync(
            $"https://api.ultramsg.com/{options.InstanceId}/messages/chat", payload, ct);

        if (!response.IsSuccessStatusCode)
            return new SendResult(false, null, $"UltraMsg HTTP {response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc  = JsonDocument.Parse(json);
        var id   = doc.RootElement.GetProperty("id").GetString();
        return new SendResult(true, id);
    }

    public Task<MessageStatusResult> GetMessageStatusAsync(string externalMessageId, CancellationToken ct = default)
        => Task.FromResult(new MessageStatusResult(externalMessageId, "sent"));

    public bool ValidateWebhookSignature(string payload, string signature)
    {
        // UltraMsg envía token en el body; validación básica
        return payload.Contains(options.Token);
    }
}

public record UltraMsgOptions(string InstanceId, string Token);
