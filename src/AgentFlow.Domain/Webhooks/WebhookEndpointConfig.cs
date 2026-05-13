namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Configuración del endpoint de un webhook configurado por el tenant para una acción.
/// Se lee desde CampaignTemplates.ActionConfigs[actionId] en runtime.
/// No es entidad EF — es un POCO para deserializar el JSON.
/// </summary>
public class WebhookEndpointConfig
{
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookMethod { get; set; } = "POST";  // GET | POST | PUT | PATCH

    /// <summary>None | ApiKey | Bearer</summary>
    public string AuthType { get; set; } = "None";

    /// <summary>Credencial en texto plano (en BD debe estar encriptada).</summary>
    public string? AuthValue { get; set; }

    /// <summary>
    /// Nombre del header para AuthType=ApiKey. Default: "X-Api-Key".
    /// Algunos tenants usan "apikey" u otro nombre.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>Headers adicionales como JSON: {"Header-Name":"value",...}</summary>
    public string? WebhookHeaders { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}
