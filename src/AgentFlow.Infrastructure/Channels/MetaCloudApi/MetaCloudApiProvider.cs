using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.Channels.MetaCloudApi;

/// <summary>
/// Proveedor de WhatsApp vía Meta Cloud API (Graph API oficial).
/// Implementa IChannelProvider igual que UltraMsg — el agente no sabe por cuál sale.
///
/// Alcance Fase B: envío de texto y media (por link) dentro de la ventana de 24h.
/// El inicio en frío con plantillas (type=template) es Fase D. La recepción de
/// entrantes/estados es el webhook único (Fase E); aquí solo se valida su firma.
///
/// Envío:  POST https://graph.facebook.com/{version}/{phone_number_id}/messages
///         Authorization: Bearer {accessToken}
/// Éxito:  HTTP 200 + { "messages": [{ "id": "wamid..." }] }
/// Error:  { "error": { "code": N, "message": "..." } }
/// </summary>
public class MetaCloudApiProvider(HttpClient http, MetaCloudApiOptions options) : IChannelProvider
{
    public ChannelType ChannelType => ChannelType.WhatsApp;
    public ProviderType ProviderType => ProviderType.MetaCloudApi;

    public async Task<SendResult> SendMessageAsync(SendMessageRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.PhoneNumberId) || string.IsNullOrWhiteSpace(options.AccessToken))
            return new SendResult(false, null, "Meta: línea sin Phone Number ID o Access Token configurados.");

        // Payload según tipo. Media por link (Meta descarga la URL).
        object payload =
            !string.IsNullOrEmpty(req.MediaUrl) && req.MediaType == "image"
                ? new { messaging_product = "whatsapp", to = req.To, type = "image", image = new { link = req.MediaUrl, caption = req.Body } }
          : !string.IsNullOrEmpty(req.MediaUrl) && req.MediaType == "document"
                ? new { messaging_product = "whatsapp", to = req.To, type = "document", document = new { link = req.MediaUrl, filename = req.Filename ?? "documento.pdf", caption = req.Body } }
          : new { messaging_product = "whatsapp", to = req.To, type = "text", text = new { body = req.Body, preview_url = false } };

        var url = $"https://graph.facebook.com/{options.GraphApiVersion}/{options.PhoneNumberId}/messages";
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (Exception ex) { return new SendResult(false, null, $"Meta: error de red ({ex.Message})"); }

        var body = await response.Content.ReadAsStringAsync(ct);
        return InterpretMetaResponse(body, response.StatusCode);
    }

    /// <summary>
    /// Interpreta la respuesta del Graph API. Éxito solo si hay messages[].id
    /// (wamid). Un body con `error` se rechaza con su mensaje, sin importar el HTTP.
    /// </summary>
    internal static SendResult InterpretMetaResponse(string json, HttpStatusCode status)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SendResult(false, null, $"Meta: respuesta vacía (HTTP {(int)status})");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex) { return new SendResult(false, null, $"Meta: JSON inválido ({ex.Message})"); }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : err.ToString();
                var code = err.TryGetProperty("code", out var c) ? c.ToString() : "";
                return new SendResult(false, null, $"Meta error {code}: {msg}");
            }

            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array && msgs.GetArrayLength() > 0)
            {
                var id = msgs[0].TryGetProperty("id", out var idp) && idp.ValueKind == JsonValueKind.String ? idp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id))
                    return new SendResult(true, id);
            }

            return new SendResult(false, null, $"Meta: respuesta sin messages[].id (HTTP {(int)status})");
        }
    }

    // Meta no expone estado por GET — el status (sent/delivered/read/failed) llega
    // por webhook. Stub coherente con UltraMsgProvider.
    public Task<MessageStatusResult> GetMessageStatusAsync(string externalMessageId, CancellationToken ct = default)
        => Task.FromResult(new MessageStatusResult(externalMessageId, "sent"));

    /// <summary>
    /// Valida la firma del webhook entrante de Meta:
    /// X-Hub-Signature-256: sha256=HMAC_SHA256(rawBody, app_secret).
    /// Comparación en tiempo constante. Sin app_secret configurado → no se valida (false).
    /// </summary>
    public bool ValidateWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrEmpty(options.AppSecret) || string.IsNullOrEmpty(signature))
            return false;

        var provided = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature["sha256=".Length..]
            : signature;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.AppSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(provided.ToLowerInvariant()));
    }
}

public record MetaCloudApiOptions(
    string PhoneNumberId,
    string AccessToken,
    string? AppSecret = null,
    string GraphApiVersion = "v21.0");
