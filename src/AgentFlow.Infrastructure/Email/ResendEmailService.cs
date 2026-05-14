using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Email;

/// <summary>
/// Implementación de IEmailService que envía vía la API REST de Resend
/// (https://resend.com). Hereda de SendGridEmailService para reutilizar TODAS
/// las plantillas HTML y los métodos públicos — solo cambia la capa de
/// transporte (SendAsync override).
///
/// Para activarla, en config: "Email:Provider": "Resend".
/// Variables requeridas:
///   Resend:ApiKey   = re_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
///   Resend:FromEmail = soporte@dominio-verificado.com
///   Resend:FromName  = TalkIA (opcional)
///
/// El dominio del FromEmail debe estar verificado en el dashboard de Resend.
/// Mientras no lo esté, Resend solo permite enviar al email del owner de la cuenta.
/// </summary>
public class ResendEmailService(
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    ILogger<ResendEmailService> log) : SendGridEmailService(config)
{
    private const string ResendApiUrl = "https://api.resend.com/emails";

    private string ApiKey =>
        config["Resend:ApiKey"] ?? throw new InvalidOperationException("Resend:ApiKey no configurada.");
    private string ResendFromEmail =>
        config["Resend:FromEmail"] ?? throw new InvalidOperationException("Resend:FromEmail no configurado.");
    private string ResendFromName =>
        config["Resend:FromName"] ?? "TalkIA";

    protected override async Task<string?> SendAsync(
        string toEmail, string toName, string subject, string htmlContent,
        CancellationToken ct,
        IEnumerable<string>? bccEmails = null,
        string? ccEmail = null)
    {
        var bccList = bccEmails?
            .Where(e => !string.IsNullOrWhiteSpace(e)
                        && !string.Equals(e, toEmail, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // El campo "from" admite el formato RFC: "Nombre <email@dominio>".
        var fromHeader = $"{ResendFromName} <{ResendFromEmail}>";

        var payload = new Dictionary<string, object?>
        {
            ["from"]    = fromHeader,
            ["to"]      = new[] { toEmail },
            ["subject"] = subject,
            ["html"]    = htmlContent,
        };
        if (!string.IsNullOrWhiteSpace(ccEmail)
            && !string.Equals(ccEmail, toEmail, StringComparison.OrdinalIgnoreCase))
        {
            payload["cc"] = new[] { ccEmail };
        }
        if (bccList is { Length: > 0 })
            payload["bcc"] = bccList;

        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsJsonAsync(ResendApiUrl, payload, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[Resend] HTTP error enviando a {Email}.", toEmail);
            throw;
        }

        if (!resp.IsSuccessStatusCode)
        {
            string body;
            try { body = await resp.Content.ReadAsStringAsync(ct); }
            catch { body = "(sin cuerpo)"; }
            var preview = body.Length > 400 ? body[..400] : body;
            log.LogError("[Resend] FAIL {Code} → {Email}: {Body}",
                (int)resp.StatusCode, toEmail, preview);
            throw new InvalidOperationException(
                $"Resend rechazó el envío a {toEmail}: HTTP {(int)resp.StatusCode}. {preview}");
        }

        // Resend devuelve { "id": "re_..." } en el body. Lo capturamos para
        // persistirlo en Message.ExternalMessageId y poder correlacionar webhooks
        // de delivery/bounce más adelante.
        string? externalId = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("id", out var idProp)
                && idProp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                externalId = idProp.GetString();
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[Resend] No se pudo parsear el id de la respuesta JSON.");
        }

        log.LogInformation("[Resend] Enviado a {Email} (subject={Subject}, bcc={BccCount}, id={Id}).",
            toEmail, subject, bccList?.Length ?? 0, externalId ?? "(sin id)");
        return externalId;
    }
}
