using System.Text.Json;

namespace AgentFlow.Infrastructure.Channels.UltraMsg;

/// <summary>
/// Cliente HTTP del endpoint <c>GET /{instance}/contacts/check</c> de UltraMsg.
/// Permite verificar si un número de teléfono tiene cuenta activa en WhatsApp
/// ANTES de gastar un envío. Cero costo en mensajes — solo consulta.
///
/// Formato de la respuesta de UltraMsg:
///   { "status": "valid",   "chatId": "5076XXXXXXXX@c.us" }
///   { "status": "invalid", "chatId": "" }
///
/// El método devuelve null cuando la red falla o el formato es inesperado —
/// el caller debe decidir si fail-open (asumir válido y dejar que el envío
/// real falle) o fail-closed (asumir inválido). Para campañas masivas se
/// recomienda fail-open en errores transitorios para no bloquear envíos
/// legítimos por flake de red.
/// </summary>
public class UltraMsgContactsChecker(HttpClient http)
{
    /// <summary>
    /// Consulta UltraMsg si el número tiene WhatsApp.
    /// </summary>
    /// <returns>
    /// true  → tiene WhatsApp (status="valid")
    /// false → NO tiene WhatsApp (status="invalid")
    /// null  → no se pudo determinar (timeout, error de red, respuesta inesperada)
    /// </returns>
    public async Task<bool?> ExistsInWhatsAppAsync(
        string instanceId, string token, string phoneE164, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneE164)) return null;

        // E.164 con +507... a chatId UltraMsg: solo dígitos + @c.us
        var digits = new string(phoneE164.Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return null;

        var normalizedInstance = instanceId.StartsWith("instance", StringComparison.OrdinalIgnoreCase)
            ? instanceId
            : $"instance{instanceId}";

        var url = $"https://api.ultramsg.com/{normalizedInstance}/contacts/check" +
                  $"?token={Uri.EscapeDataString(token)}&chatId={digits}@c.us";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            var response = await http.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("status", out var statusProp)) return null;
            var status = statusProp.GetString()?.Trim().ToLowerInvariant();

            return status switch
            {
                "valid"   => true,
                "invalid" => false,
                _         => null
            };
        }
        catch
        {
            // Best-effort: cualquier error es "no determinable".
            return null;
        }
    }
}
