namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Ejecuta el HTTP call al webhook del tenant usando la configuración del endpoint
/// y el payload construido por PayloadBuilder.
/// Maneja timeout, autenticación, headers extra y errores de red.
/// </summary>
public interface IHttpDispatcher
{
    Task<HttpDispatchResult> SendAsync(
        WebhookEndpointConfig endpointConfig,
        object payload,
        string contentType,
        CancellationToken ct = default);
}
