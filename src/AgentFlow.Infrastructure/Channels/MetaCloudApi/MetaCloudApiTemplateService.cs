using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.MetaCloudApi;

/// <summary>
/// Operaciones sobre plantillas (HSM) de Meta vía Graph API, a nivel de WABA:
///   • Crear   → POST   /{WABA_ID}/message_templates
///   • Listar  → GET    /{WABA_ID}/message_templates  (para "Sincronizar")
///   • Borrar  → DELETE /{WABA_ID}/message_templates?name=
///
/// BLINDAJE POR-TENANT: el servicio NO tiene credenciales propias. Recibe
/// <c>wabaId</c> + <c>accessToken</c> como parámetros en cada llamada — siempre los de
/// la línea del tenant (cliente). Así una plantilla de un cliente jamás se opera con el
/// token o WABA de otro. El llamador (controller) resuelve esos valores filtrando por
/// el TenantId del JWT antes de invocar acá.
/// </summary>
public interface IMetaTemplateService
{
    Task<MetaTemplateResult> CreateAsync(
        string wabaId, string accessToken, MetaTemplateInput input, CancellationToken ct = default);

    Task<IReadOnlyList<MetaRemoteTemplate>> ListAsync(
        string wabaId, string accessToken, CancellationToken ct = default);

    Task<MetaTemplateResult> DeleteAsync(
        string wabaId, string accessToken, string name, CancellationToken ct = default);
}

/// <summary>Datos para crear una plantilla (Fase 1: HEADER texto, BODY, FOOTER).</summary>
public record MetaTemplateInput(
    string Name,
    string Language,
    string Category,
    string? HeaderText,
    string BodyText,
    string? FooterText,
    IReadOnlyList<string> HeaderSamples,
    IReadOnlyList<string> BodySamples);

/// <param name="Success">true si Meta aceptó la operación.</param>
/// <param name="MetaTemplateId">id devuelto por Meta al crear.</param>
/// <param name="Status">estado inicial que reporta Meta (típicamente PENDING).</param>
/// <param name="Error">mensaje de error de Meta tal cual, para mostrar al usuario.</param>
public record MetaTemplateResult(bool Success, string? MetaTemplateId, string? Status, string? Error);

public record MetaRemoteTemplate(
    string Name, string Language, string Category, string Status, string? Id, string? RejectedReason,
    string? HeaderText = null, string? BodyText = null, string? FooterText = null);

public class MetaCloudApiTemplateService(HttpClient http, ILogger<MetaCloudApiTemplateService> logger)
    : IMetaTemplateService
{
    private const string GraphApiVersion = "v21.0";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<MetaTemplateResult> CreateAsync(
        string wabaId, string accessToken, MetaTemplateInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(accessToken))
            return new MetaTemplateResult(false, null, null, "Línea Meta sin WABA o Access Token configurados.");

        var components = BuildComponents(input);
        var payload = new Dictionary<string, object?>
        {
            ["name"] = input.Name,
            ["language"] = input.Language,
            ["category"] = input.Category,
            ["components"] = components,
        };

        var url = $"https://graph.facebook.com/{GraphApiVersion}/{wabaId}/message_templates";
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        string body;
        System.Net.HttpStatusCode status;
        try
        {
            var resp = await http.SendAsync(request, ct);
            status = resp.StatusCode;
            body = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[MetaTemplates] Error de red al crear plantilla {Name}.", input.Name);
            return new MetaTemplateResult(false, null, null, $"Error de red: {ex.Message}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                return new MetaTemplateResult(false, null, null, FormatMetaError(err));

            var id = root.TryGetProperty("id", out var idp) ? idp.GetString() : null;
            var st = root.TryGetProperty("status", out var stp) ? stp.GetString() : "PENDING";
            if (!string.IsNullOrWhiteSpace(id))
                return new MetaTemplateResult(true, id, st ?? "PENDING", null);

            return new MetaTemplateResult(false, null, null, $"Respuesta inesperada de Meta (HTTP {(int)status}): {body}");
        }
        catch (Exception ex)
        {
            return new MetaTemplateResult(false, null, null, $"Respuesta no parseable de Meta: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<MetaRemoteTemplate>> ListAsync(
        string wabaId, string accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(accessToken))
            return [];

        var url = $"https://graph.facebook.com/{GraphApiVersion}/{wabaId}/message_templates" +
                  "?fields=name,status,category,language,id,rejected_reason,components&limit=250";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        string body;
        try
        {
            var resp = await http.SendAsync(request, ct);
            body = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[MetaTemplates] Error de red al listar plantillas de WABA {Waba}.", wabaId);
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<MetaRemoteTemplate>();
            foreach (var t in data.EnumerateArray())
            {
                var name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var lang = t.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
                var cat = t.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                var st = t.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                var id = t.TryGetProperty("id", out var i) ? i.GetString() : null;
                var rej = t.TryGetProperty("rejected_reason", out var r) ? r.GetString() : null;
                var (hdr, bdy, ftr) = ParseComponents(t);
                if (!string.IsNullOrEmpty(name))
                    list.Add(new MetaRemoteTemplate(name, lang, cat, st, id, rej, hdr, bdy, ftr));
            }
            return list;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[MetaTemplates] Respuesta no parseable al listar plantillas.");
            return [];
        }
    }

    public async Task<MetaTemplateResult> DeleteAsync(
        string wabaId, string accessToken, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(accessToken))
            return new MetaTemplateResult(false, null, null, "Línea Meta sin WABA o Access Token.");

        var url = $"https://graph.facebook.com/{GraphApiVersion}/{wabaId}/message_templates?name={Uri.EscapeDataString(name)}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var resp = await http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : err.ToString();
                return new MetaTemplateResult(false, null, null, $"Meta: {msg}");
            }
            return new MetaTemplateResult(true, null, null, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[MetaTemplates] Error borrando plantilla {Name}.", name);
            return new MetaTemplateResult(false, null, null, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Extrae el texto de HEADER (solo format=TEXT), BODY y FOOTER del array
    /// <c>components</c> que devuelve Meta al listar. Componentes de media/botones
    /// se ignoran en Fase 1 (HeaderText queda null si el encabezado no es texto).
    /// </summary>
    private static (string? Header, string? Body, string? Footer) ParseComponents(JsonElement t)
    {
        if (!t.TryGetProperty("components", out var comps) || comps.ValueKind != JsonValueKind.Array)
            return (null, null, null);

        string? header = null, body = null, footer = null;
        foreach (var c in comps.EnumerateArray())
        {
            var type = c.TryGetProperty("type", out var ty) ? ty.GetString()?.ToUpperInvariant() : null;
            var text = c.TryGetProperty("text", out var tx) ? tx.GetString() : null;
            switch (type)
            {
                case "HEADER":
                    var format = c.TryGetProperty("format", out var fm) ? fm.GetString()?.ToUpperInvariant() : null;
                    if (format == "TEXT") header = text;
                    break;
                case "BODY": body = text; break;
                case "FOOTER": footer = text; break;
            }
        }
        return (header, body, footer);
    }

    /// <summary>
    /// Construye el array <c>components</c> que espera Meta. Solo agrega "example"
    /// cuando el componente tiene variables (Meta rechaza ejemplos vacíos o faltantes).
    /// </summary>
    private static List<object> BuildComponents(MetaTemplateInput input)
    {
        var components = new List<object>();

        // HEADER (texto) — opcional, máximo 1 variable.
        if (!string.IsNullOrWhiteSpace(input.HeaderText))
        {
            var header = new Dictionary<string, object?>
            {
                ["type"] = "HEADER",
                ["format"] = "TEXT",
                // Meta NO admite formato (negritas *, itálicas _, etc.) en el HEADER.
                // Lo limpiamos para que no rechace con "Invalid parameter".
                ["text"] = StripWhatsAppFormatting(input.HeaderText),
            };
            if (input.HeaderSamples.Count > 0)
                header["example"] = new Dictionary<string, object?> { ["header_text"] = input.HeaderSamples };
            components.Add(header);
        }

        // BODY — requerido.
        var bodyComp = new Dictionary<string, object?>
        {
            ["type"] = "BODY",
            ["text"] = input.BodyText,
        };
        if (input.BodySamples.Count > 0)
            // body_text es un array de "sets" de ejemplo; mandamos un set con los valores.
            bodyComp["example"] = new Dictionary<string, object?> { ["body_text"] = new[] { input.BodySamples } };
        components.Add(bodyComp);

        // FOOTER — opcional, sin variables.
        if (!string.IsNullOrWhiteSpace(input.FooterText))
            components.Add(new Dictionary<string, object?> { ["type"] = "FOOTER", ["text"] = input.FooterText });

        return components;
    }

    /// <summary>
    /// Quita los caracteres de formato de WhatsApp (*negrita*, _itálica_, ~tachado~,
    /// `mono`) del texto. El HEADER de Meta NO admite formato — si lo dejamos, Meta
    /// rechaza con error 100. El BODY sí lo admite, así que esto solo se aplica al header.
    /// </summary>
    private static string StripWhatsAppFormatting(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (ch is not ('*' or '_' or '~' or '`')) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>
    /// Arma un mensaje de error legible a partir del objeto <c>error</c> de Meta.
    /// El campo <c>message</c> suele ser genérico ("Invalid parameter"); el detalle
    /// accionable está en <c>error_user_title</c>, <c>error_user_msg</c> y
    /// <c>error_data.details</c>. Los incluimos para que el usuario sepa QUÉ corregir.
    /// </summary>
    private static string FormatMetaError(JsonElement err)
    {
        string? message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
        string code = err.TryGetProperty("code", out var c) ? c.ToString() : "";
        string? userTitle = err.TryGetProperty("error_user_title", out var ut) ? ut.GetString() : null;
        string? userMsg = err.TryGetProperty("error_user_msg", out var um) ? um.GetString() : null;
        string? details = null;
        if (err.TryGetProperty("error_data", out var ed) && ed.ValueKind == JsonValueKind.Object
            && ed.TryGetProperty("details", out var d))
            details = d.GetString();

        // Preferir el detalle más específico que dé Meta.
        var specific = !string.IsNullOrWhiteSpace(userMsg) ? userMsg
                     : !string.IsNullOrWhiteSpace(details) ? details
                     : userTitle;
        var baseMsg = !string.IsNullOrWhiteSpace(message) ? message : "Error de validación de Meta";

        if (!string.IsNullOrWhiteSpace(specific) && !string.Equals(specific, baseMsg, StringComparison.OrdinalIgnoreCase))
            return $"Meta error {code}: {baseMsg} — {specific}";
        return $"Meta error {code}: {baseMsg}";
    }
}
