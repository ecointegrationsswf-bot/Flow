using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Contrato único para todos los canales de comunicación.
/// UltraMsg, Meta Cloud API y SMS implementan esta interfaz.
/// El agente nunca sabe por qué canal está enviando.
/// </summary>
public interface IChannelProvider
{
    ChannelType ChannelType { get; }
    ProviderType ProviderType { get; }

    Task<SendResult> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default);
    Task<MessageStatusResult> GetMessageStatusAsync(string externalMessageId, CancellationToken ct = default);
    bool ValidateWebhookSignature(string payload, string signature);
}

public record SendMessageRequest(
    string To,
    string Body,
    string? MediaUrl = null,
    string? ExternalId = null
);

public record SendResult(
    bool Success,
    string? ExternalMessageId,
    string? Error = null
);

public record MessageStatusResult(
    string ExternalMessageId,
    string Status   // sent | delivered | read | failed
);
