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
    string? MediaType = null,   // "image" | "document" | "audio"
    string? Filename = null,    // nombre de archivo para documentos
    string? ExternalId = null,
    // ── Envío como plantilla de Meta (type=template) ──────────────────────
    // Si TemplateName != null, el provider Meta arma el payload de plantilla
    // (arranque en frío fuera de la ventana de 24h) en vez de texto libre.
    // UltraMsg ignora estos campos. Los parámetros van en el orden {{1}},{{2}}…
    string? TemplateName = null,
    string? TemplateLanguage = null,
    IReadOnlyList<string>? TemplateBodyParams = null,
    string? TemplateHeaderParam = null
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
