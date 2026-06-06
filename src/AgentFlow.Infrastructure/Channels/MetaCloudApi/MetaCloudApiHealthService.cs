using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.MetaCloudApi;

/// <summary>
/// Chequeo de salud de una línea Meta Cloud API, equivalente al
/// <c>IUltraMsgInstanceService.GetStatusAsync</c> de UltraMsg.
///
/// Meta NO expone un "instance/status" como UltraMsg. El equivalente más cercano
/// y barato es consultar el propio Phone Number en el Graph API:
///   GET https://graph.facebook.com/{version}/{phone_number_id}
///       ?fields=health_status,quality_rating,verified_name
///   Authorization: Bearer {accessToken}
///
/// Señales:
///   • HTTP 200 + health_status.can_send_message ∈ {AVAILABLE, LIMITED} → la línea
///     puede enviar (LIMITED solo significa tope de mensajería, NO caída).
///   • can_send_message == BLOCKED → la línea está bloqueada por Meta.
///   • error.code 190 (o HTTP 401) → el Access Token venció / es inválido.
///   • Otro error / HTTP 4xx-5xx → problema no determinable.
///
/// Para que los consumidores existentes (dispatcher, follow-up sweep, job diario)
/// no tengan que distinguir proveedores en su comparación, normalizamos el estado
/// sano al MISMO string que UltraMsg: "authenticated". Así el resto del código que
/// hace <c>string.Equals(status, "authenticated")</c> funciona sin cambios.
/// </summary>
public interface IMetaCloudApiHealthService
{
    Task<MetaLineHealth> GetHealthAsync(
        string phoneNumberId, string accessToken, string? graphApiVersion = null,
        CancellationToken ct = default);
}

/// <param name="Status">
/// Normalizado: "authenticated" (sana) | "blocked" | "token_invalid" |
/// "unconfigured" | "unknown". Se persiste en <c>WhatsAppLine.LastStatus</c>.
/// </param>
/// <param name="CanSend">true si la línea puede enviar mensajes ahora.</param>
/// <param name="Detail">Texto humano para logs/correos (ej: el mensaje de error de Meta).</param>
public record MetaLineHealth(string Status, bool CanSend, string? Detail = null);

public class MetaCloudApiHealthService(HttpClient http, ILogger<MetaCloudApiHealthService> logger)
    : IMetaCloudApiHealthService
{
    private const string DefaultVersion = "v21.0";

    public async Task<MetaLineHealth> GetHealthAsync(
        string phoneNumberId, string accessToken, string? graphApiVersion = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(accessToken))
            return new MetaLineHealth("unconfigured", false, "Línea Meta sin Phone Number ID o Access Token.");

        var version = string.IsNullOrWhiteSpace(graphApiVersion) ? DefaultVersion : graphApiVersion;
        var url = $"https://graph.facebook.com/{version}/{phoneNumberId}" +
                  "?fields=health_status,quality_rating,verified_name";

        HttpResponseMessage response;
        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            response = await http.SendAsync(request, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            // Error de red / timeout: NO podemos confirmar que esté sana. Devolvemos
            // "unknown" + CanSend=false; el consumidor aplica su política fail-closed.
            logger.LogWarning(ex, "[MetaHealth] Error de red consultando salud de {PhoneNumberId}.", phoneNumberId);
            return new MetaLineHealth("unknown", false, $"Error de red: {ex.Message}");
        }

        return Interpret(body, response.StatusCode);
    }

    /// <summary>
    /// Traduce la respuesta del Graph API a un <see cref="MetaLineHealth"/> normalizado.
    /// Expuesto internal para test unitario sin red.
    /// </summary>
    internal static MetaLineHealth Interpret(string json, HttpStatusCode status)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MetaLineHealth("unknown", false, $"Respuesta vacía (HTTP {(int)status})");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex) { return new MetaLineHealth("unknown", false, $"JSON inválido ({ex.Message})"); }

        using (doc)
        {
            var root = doc.RootElement;

            // ── Error explícito de Meta ───────────────────────────────────
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
                var msg = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : err.ToString();

                // 190 = token vencido/inválido; 102 = sesión inválida; 10/200 = permisos.
                if (code is 190 or 102 || status == HttpStatusCode.Unauthorized)
                    return new MetaLineHealth("token_invalid", false, $"Token inválido/expirado (code {code}): {msg}");

                return new MetaLineHealth("error", false, $"Meta error {code}: {msg}");
            }

            // ── health_status.can_send_message ────────────────────────────
            if (root.TryGetProperty("health_status", out var hs) && hs.ValueKind == JsonValueKind.Object
                && hs.TryGetProperty("can_send_message", out var csm) && csm.ValueKind == JsonValueKind.String)
            {
                var value = (csm.GetString() ?? "").ToUpperInvariant();
                return value switch
                {
                    // LIMITED = puede enviar pero con tope de mensajería → sigue siendo sana.
                    "AVAILABLE" or "LIMITED" => new MetaLineHealth("authenticated", true, $"can_send_message={value}"),
                    "BLOCKED" => new MetaLineHealth("blocked", false, "Meta marcó la línea como BLOCKED."),
                    _ => new MetaLineHealth("unknown", false, $"can_send_message desconocido: {value}"),
                };
            }

            // ── Sin health_status pero HTTP 200 ───────────────────────────
            // El token resolvió el Phone Number (existe + permisos OK). Suficiente
            // para considerar la línea operativa aunque Meta no devuelva health_status
            // (el campo requiere ciertos permisos / no siempre viene).
            if (status == HttpStatusCode.OK)
                return new MetaLineHealth("authenticated", true, "HTTP 200 sin health_status (token válido).");

            return new MetaLineHealth("unknown", false, $"Respuesta inesperada (HTTP {(int)status}).");
        }
    }
}
