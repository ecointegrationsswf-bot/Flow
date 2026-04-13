using System.Text.Json;
using AgentFlow.Domain.Webhooks;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Lee la configuración de una acción desde el JSON ActionConfigs de un CampaignTemplate.
///
/// El JSON tiene formato:
/// {
///   "action-id-guid": {
///     "webhookUrl": "...",
///     "webhookMethod": "POST",
///     "webhookHeaders": "{...}",
///     "authType": "Bearer",
///     "authValue": "...",
///     "timeoutSeconds": 10,
///     "inputSchema": { ... },
///     "outputSchema": { ... }
///   }
/// }
///
/// Extrae: WebhookEndpointConfig, InputSchema, OutputSchema y el contentType
/// para que el ActionExecutorService pueda invocar el flujo completo.
/// </summary>
public class ActionConfigReader(ILogger<ActionConfigReader> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Extrae la configuración de una acción específica del JSON completo de ActionConfigs.
    /// Devuelve null si la acción no está configurada.
    /// </summary>
    public ActionConfigBundle? Read(string? actionConfigsJson, Guid actionId)
    {
        if (string.IsNullOrWhiteSpace(actionConfigsJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(actionConfigsJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;

            // Buscar el objeto con key = actionId (como string lowercase)
            var key = actionId.ToString();
            JsonElement actionEl = default;
            bool found = false;

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    actionEl = prop.Value;
                    found = true;
                    break;
                }
            }

            if (!found || actionEl.ValueKind != JsonValueKind.Object) return null;

            // Raw JSON de la config de esta acción
            var rawJson = actionEl.GetRawText();

            // Deserializar WebhookEndpointConfig
            var endpointConfig = new WebhookEndpointConfig
            {
                WebhookUrl = GetString(actionEl, "webhookUrl") ?? "",
                WebhookMethod = GetString(actionEl, "webhookMethod") ?? "POST",
                WebhookHeaders = GetString(actionEl, "webhookHeaders"),
                AuthType = GetString(actionEl, "authType") ?? "None",
                AuthValue = GetString(actionEl, "authValue"),
                ApiKeyHeaderName = GetString(actionEl, "apiKeyHeaderName") ?? "X-Api-Key",
                TimeoutSeconds = GetInt(actionEl, "timeoutSeconds") ?? 10
            };

            // Deserializar InputSchema (opcional)
            InputSchema? inputSchema = null;
            if (actionEl.TryGetProperty("inputSchema", out var inputEl) && inputEl.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    inputSchema = JsonSerializer.Deserialize<InputSchema>(inputEl.GetRawText(), JsonOpts);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("[ActionConfigReader] inputSchema inválido para action {ActionId}: {Message}",
                        actionId, ex.Message);
                }
            }

            // Deserializar OutputSchema (opcional)
            OutputSchema? outputSchema = null;
            if (actionEl.TryGetProperty("outputSchema", out var outputEl) && outputEl.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    outputSchema = JsonSerializer.Deserialize<OutputSchema>(outputEl.GetRawText(), JsonOpts);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("[ActionConfigReader] outputSchema inválido para action {ActionId}: {Message}",
                        actionId, ex.Message);
                }
            }

            // Action Trigger Protocol — Fase 1: TriggerConfig opcional.
            // Si el JSON no tiene la key o es inválido, seguimos sin él (retrocompat).
            TriggerConfig? triggerConfig = null;
            if (actionEl.TryGetProperty("triggerConfig", out var triggerEl) && triggerEl.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    triggerConfig = JsonSerializer.Deserialize<TriggerConfig>(triggerEl.GetRawText(), JsonOpts);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("[ActionConfigReader] triggerConfig inválido para action {ActionId}: {Message}",
                        actionId, ex.Message);
                }
            }

            return new ActionConfigBundle
            {
                EndpointConfig = endpointConfig,
                InputSchema = inputSchema,
                OutputSchema = outputSchema,
                TriggerConfig = triggerConfig
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning("[ActionConfigReader] Error leyendo ActionConfigs: {Message}", ex.Message);
            return null;
        }
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Null => null,
            _ => prop.ToString()
        };
    }

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n) ? n : null;
    }
}

/// <summary>
/// Bundle de la configuración de una acción leída del JSON.
/// </summary>
public class ActionConfigBundle
{
    public WebhookEndpointConfig EndpointConfig { get; init; } = new();
    public InputSchema? InputSchema { get; init; }
    public OutputSchema? OutputSchema { get; init; }

    /// <summary>
    /// Action Trigger Protocol — Capa 1. Opcional: presente solo si el admin
    /// configuró el "Paso 0 Trigger" en el Webhook Builder (Fase 5) o agregó
    /// las keys manualmente al JSON ActionConfigs.
    /// </summary>
    public TriggerConfig? TriggerConfig { get; init; }
}

/// <summary>
/// POCO plano para deserializar ActionDefinition.DefaultWebhookContract (JSON).
/// Misma forma que un entry de CampaignTemplate.ActionConfigs[actionId] pero sin la
/// anidación de WebhookEndpointConfig — todos los campos al primer nivel del JSON.
/// </summary>
public class ActionConfigBundleJson
{
    public string? WebhookUrl { get; init; }
    public string? WebhookMethod { get; init; }
    public string? ContentType { get; init; }
    public string? Structure { get; init; }
    public string? AuthType { get; init; }
    public string? AuthValue { get; init; }
    public string? ApiKeyHeaderName { get; init; }
    public string? WebhookHeaders { get; init; }
    public int? TimeoutSeconds { get; init; }
    public InputSchema? InputSchema { get; init; }
    public OutputSchema? OutputSchema { get; init; }
    public TriggerConfig? TriggerConfig { get; init; }
}
