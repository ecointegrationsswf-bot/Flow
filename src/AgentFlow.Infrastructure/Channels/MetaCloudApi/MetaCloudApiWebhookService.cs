using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.MetaCloudApi;

/// <summary>
/// Lectura (solo GET) del estado de suscripción de webhook de una WABA en el
/// Graph API de Meta. Es puramente informativo / de diagnóstico — NO modifica
/// nada en Meta.
///
///   GET https://graph.facebook.com/{version}/{waba_id}/subscribed_apps
///       Authorization: Bearer {accessToken}
///
/// El webhook de Meta es a nivel de App (una sola Callback URL para todas las
/// WABAs y números de esa App). Una WABA debe estar "suscrita" a la App para que
/// sus eventos lleguen. Opcionalmente una WABA puede tener un override
/// (<c>override_callback_uri</c>) que apunte a una URL distinta de la de la App.
///
/// Señales:
///   • HTTP 200 + data[] no vacío → la WABA está suscrita a al menos una App.
///   • override_callback_uri presente → la WABA usa un override (URL específica).
///   • override_callback_uri ausente → usa el webhook a nivel de App (lo normal).
///   • error.code 190 / HTTP 401 → token vencido/ inválido.
///   • HTTP 403 / code 200 (permiso) → el token no tiene whatsapp_business_management.
/// </summary>
public interface IMetaCloudApiWebhookService
{
    Task<MetaWebhookSubscription> GetSubscriptionAsync(
        string wabaId, string accessToken, string? graphApiVersion = null,
        CancellationToken ct = default);

    /// <summary>
    /// Suscribe la App (la del token) a la WABA y fija un override de callback que
    /// apunta a <paramref name="callbackUrl"/>. Meta valida la URL con un GET de
    /// verificación usando <paramref name="verifyToken"/> en el momento del POST,
    /// así que la URL debe ser pública (HTTPS) y responder el challenge.
    ///   POST https://graph.facebook.com/{version}/{waba_id}/subscribed_apps
    ///        override_callback_uri={callbackUrl}&amp;verify_token={verifyToken}
    /// </summary>
    Task<MetaWebhookWriteResult> SetSubscriptionAsync(
        string wabaId, string accessToken, string callbackUrl, string verifyToken,
        string? graphApiVersion = null, CancellationToken ct = default);
}

/// <param name="Ok">true si se pudo leer el estado (HTTP 200). false = error (ver Status/Detail).</param>
/// <param name="Status">"ok" | "token_invalid" | "no_permission" | "unconfigured" | "unknown".</param>
/// <param name="IsSubscribed">true si la WABA está suscrita a al menos una App.</param>
/// <param name="OverrideCallbackUri">URL de override de la WABA, si existe (null = usa el webhook de la App).</param>
/// <param name="AppName">Nombre de la App suscrita (si Meta lo reporta).</param>
/// <param name="Detail">Texto humano para la UI/logs.</param>
public record MetaWebhookSubscription(
    bool Ok,
    string Status,
    bool IsSubscribed,
    string? OverrideCallbackUri,
    string? AppName,
    string? Detail = null);

/// <param name="Ok">true si Meta confirmó la configuración (success:true).</param>
/// <param name="Status">"ok" | "token_invalid" | "no_permission" | "verify_failed" | "unconfigured" | "unknown".</param>
/// <param name="Detail">Texto humano para la UI/logs.</param>
public record MetaWebhookWriteResult(bool Ok, string Status, string? Detail = null);

public class MetaCloudApiWebhookService(HttpClient http, ILogger<MetaCloudApiWebhookService> logger)
    : IMetaCloudApiWebhookService
{
    private const string DefaultVersion = "v21.0";

    public async Task<MetaWebhookSubscription> GetSubscriptionAsync(
        string wabaId, string accessToken, string? graphApiVersion = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(accessToken))
            return new MetaWebhookSubscription(false, "unconfigured", false, null, null,
                "Línea Meta sin WABA ID o Access Token.");

        var version = string.IsNullOrWhiteSpace(graphApiVersion) ? DefaultVersion : graphApiVersion;
        var url = $"https://graph.facebook.com/{version}/{wabaId}/subscribed_apps";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!response.IsSuccessStatusCode)
            {
                // Distinguir token inválido vs falta de permiso vs otro error.
                int code = -1; string? msg = null;
                if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                {
                    if (err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number) code = c.GetInt32();
                    msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || code == 190)
                    return new MetaWebhookSubscription(false, "token_invalid", false, null, null,
                        msg ?? "El Access Token venció o es inválido.");

                if (response.StatusCode == HttpStatusCode.Forbidden || code is 200 or 10 or 803)
                    return new MetaWebhookSubscription(false, "no_permission", false, null, null,
                        msg ?? "El token no tiene permiso whatsapp_business_management para leer la suscripción.");

                logger.LogWarning("[Meta][subscribed_apps] HTTP {Code} leyendo WABA {Waba}: {Msg}",
                    (int)response.StatusCode, wabaId, msg);
                return new MetaWebhookSubscription(false, "unknown", false, null, null,
                    msg ?? $"Error HTTP {(int)response.StatusCode} consultando Meta.");
            }

            // HTTP 200 → data[] de Apps suscritas.
            var isSubscribed = false;
            string? overrideUri = null;
            string? appName = null;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                isSubscribed = true;
                var first = data[0];
                if (first.TryGetProperty("override_callback_uri", out var ocu) && ocu.ValueKind == JsonValueKind.String)
                    overrideUri = ocu.GetString();
                if (first.TryGetProperty("whatsapp_business_api_data", out var wad) && wad.ValueKind == JsonValueKind.Object
                    && wad.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                    appName = nm.GetString();
            }

            var detail = isSubscribed
                ? (overrideUri is null
                    ? "WABA suscrita; usa el webhook a nivel de App."
                    : "WABA suscrita con override de callback propio.")
                : "La WABA no está suscrita a ninguna App.";

            return new MetaWebhookSubscription(true, "ok", isSubscribed, overrideUri, appName, detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Meta][subscribed_apps] Error consultando WABA {Waba}", wabaId);
            return new MetaWebhookSubscription(false, "unknown", false, null, null,
                "No se pudo consultar Meta (red o respuesta inesperada).");
        }
    }

    public async Task<MetaWebhookWriteResult> SetSubscriptionAsync(
        string wabaId, string accessToken, string callbackUrl, string verifyToken,
        string? graphApiVersion = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(accessToken))
            return new MetaWebhookWriteResult(false, "unconfigured", "Línea Meta sin WABA ID o Access Token.");
        if (string.IsNullOrWhiteSpace(callbackUrl) || string.IsNullOrWhiteSpace(verifyToken))
            return new MetaWebhookWriteResult(false, "unconfigured", "Falta la URL de callback o el verify token.");

        var version = string.IsNullOrWhiteSpace(graphApiVersion) ? DefaultVersion : graphApiVersion;
        var url = $"https://graph.facebook.com/{version}/{wabaId}/subscribed_apps";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["override_callback_uri"] = callbackUrl,
                ["verify_token"] = verifyToken,
            });

            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (response.IsSuccessStatusCode)
            {
                var success = root.TryGetProperty("success", out var s)
                    && (s.ValueKind == JsonValueKind.True
                        || (s.ValueKind == JsonValueKind.String && string.Equals(s.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
                return success
                    ? new MetaWebhookWriteResult(true, "ok", "Webhook configurado: la WABA quedó suscrita y apuntando a la URL indicada.")
                    : new MetaWebhookWriteResult(false, "unknown", "Meta respondió sin success=true.");
            }

            // Error → distinguir token / permiso / validación de URL.
            int code = -1, subcode = -1; string? msg = null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                if (err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number) code = c.GetInt32();
                if (err.TryGetProperty("error_subcode", out var sc) && sc.ValueKind == JsonValueKind.Number) subcode = sc.GetInt32();
                msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || code == 190)
                return new MetaWebhookWriteResult(false, "token_invalid", msg ?? "El Access Token venció o es inválido.");

            if (response.StatusCode == HttpStatusCode.Forbidden || code is 200 or 10 or 803)
                return new MetaWebhookWriteResult(false, "no_permission",
                    msg ?? "El token no tiene permiso whatsapp_business_management de admin sobre la WABA.");

            // Falla típica de verificación del callback (URL no responde el challenge).
            if (code is 2200 or 100 || (msg?.Contains("verif", StringComparison.OrdinalIgnoreCase) ?? false))
                return new MetaWebhookWriteResult(false, "verify_failed",
                    msg ?? "Meta no pudo verificar la URL (¿es pública HTTPS y responde el challenge con el verify token?).");

            logger.LogWarning("[Meta][subscribed_apps][POST] HTTP {Code}/{Sub} WABA {Waba}: {Msg}",
                (int)response.StatusCode, subcode, wabaId, msg);
            return new MetaWebhookWriteResult(false, "unknown", msg ?? $"Error HTTP {(int)response.StatusCode} configurando en Meta.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Meta][subscribed_apps][POST] Error configurando WABA {Waba}", wabaId);
            return new MetaWebhookWriteResult(false, "unknown", "No se pudo configurar en Meta (red o respuesta inesperada).");
        }
    }
}
