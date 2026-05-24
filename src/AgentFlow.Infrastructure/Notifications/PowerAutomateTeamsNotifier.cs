using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Notifications;

/// <summary>
/// Implementación del <see cref="ITeamsNotifier"/> que postea al webhook de
/// Power Automate (Microsoft Teams). El payload del flow espera el shape
/// <c>{"message": "&lt;texto&gt;"}</c>.
///
/// Configuración en <c>appsettings.json</c>:
/// <code>
/// "Notifications": {
///   "Teams": {
///     "WebhookUrl": "https://...powerplatform.com:443/powerautomate/.../invoke?api-version=1&amp;sp=...&amp;sv=...&amp;sig=..."
///   }
/// }
/// </code>
///
/// Si la URL no está configurada el notificador queda en modo NoOp silencioso
/// — útil para entornos de desarrollo donde no se quiere ruido en el canal real.
///
/// Para que el envío NO bloquee el flujo principal: timeout corto (10s),
/// catch de TODA excepción, y log a nivel Warning (nunca Error) porque las
/// notificaciones son siempre informativas.
/// </summary>
public class PowerAutomateTeamsNotifier(
    HttpClient http,
    IConfiguration config,
    ILogger<PowerAutomateTeamsNotifier> logger) : ITeamsNotifier
{
    private readonly string? _webhookUrl = config["Notifications:Teams:WebhookUrl"];

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_webhookUrl);

    public async Task NotifyAsync(string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            logger.LogDebug("[TeamsNotifier] Webhook no configurado, mensaje descartado: {Message}",
                message?.Length > 80 ? message[..80] + "…" : message);
            return;
        }

        // Defensivamente truncamos a 4000 chars para no exceder el límite de
        // Power Automate (tampoco tiene sentido un alert de 10K caracteres).
        var safeMessage = string.IsNullOrEmpty(message)
            ? "(mensaje vacío)"
            : (message.Length > 4000 ? message[..4000] + "…[truncado]" : message);

        // Construir el body manualmente: Power Automate es estricto con el shape.
        // Usar JsonSerializer para escapar caracteres especiales correctamente.
        var payload = new { message = safeMessage };
        var jsonBody = JsonSerializer.Serialize(payload);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var response = await http.PostAsync(_webhookUrl, content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cts.Token);
                logger.LogWarning(
                    "[TeamsNotifier] Power Automate respondió {Status}: {Body}",
                    response.StatusCode, body?.Length > 200 ? body[..200] : body);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: jamás lanzamos. El caller no debe enterarse de que
            // la notificación falló — su flujo principal sigue intacto.
            logger.LogWarning(ex, "[TeamsNotifier] No se pudo postear al webhook de Teams.");
        }
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return null; }
    }
}
