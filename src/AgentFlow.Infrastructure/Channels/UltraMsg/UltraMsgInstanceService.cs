using System.Text.Json;

namespace AgentFlow.Infrastructure.Channels.UltraMsg;

public class UltraMsgInstanceService(HttpClient http) : IUltraMsgInstanceService
{
    private const string BaseUrl = "https://api.ultramsg.com";

    /// <summary>
    /// Normaliza el instanceId: UltraMsg espera "instance{numero}" en la URL.
    /// Si el usuario pone solo "140984", se convierte a "instance140984".
    /// </summary>
    private static string NormalizeInstanceId(string instanceId)
    {
        instanceId = instanceId.Trim();
        return instanceId.StartsWith("instance", StringComparison.OrdinalIgnoreCase)
            ? instanceId
            : $"instance{instanceId}";
    }

    public async Task<UltraMsgInstanceStatus> GetStatusAsync(string instanceId, string token, CancellationToken ct = default)
    {
        var normalizedId = NormalizeInstanceId(instanceId);
        var url = $"{BaseUrl}/{normalizedId}/instance/status?token={token}";
        var response = await http.GetAsync(url, ct);

        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Intentar parsear error de UltraMsg
            return new UltraMsgInstanceStatus("disconnected", null);
        }

        var doc = JsonDocument.Parse(json);

        // UltraMsg devuelve estructura anidada:
        // {"status":{"accountStatus":{"status":"authenticated","substatus":"connected"}}}
        string status = "unknown";
        string? phone = null;

        if (doc.RootElement.TryGetProperty("status", out var statusObj))
        {
            if (statusObj.ValueKind == JsonValueKind.String)
            {
                // Formato plano: {"status":"qr"}
                status = statusObj.GetString() ?? "unknown";
            }
            else if (statusObj.ValueKind == JsonValueKind.Object)
            {
                // Formato anidado: {"status":{"accountStatus":{"status":"authenticated","substatus":"connected"}}}
                if (statusObj.TryGetProperty("accountStatus", out var acctStatus))
                {
                    if (acctStatus.TryGetProperty("status", out var innerStatus))
                        status = innerStatus.GetString() ?? "unknown";
                    if (acctStatus.TryGetProperty("substatus", out var substatus))
                    {
                        var sub = substatus.GetString();
                        // Si el substatus es "connected" con status "authenticated", reportar como "authenticated"
                        if (sub == "connected" && status == "authenticated")
                            status = "authenticated";
                    }
                }
                // Buscar telefono en status.phone o status.accountStatus.phone
                if (statusObj.TryGetProperty("phone", out var phoneProp2))
                    phone = phoneProp2.GetString();
            }
        }

        // Buscar telefono en raiz tambien
        if (phone == null && doc.RootElement.TryGetProperty("phone", out var phoneProp))
            phone = phoneProp.GetString();

        return new UltraMsgInstanceStatus(status, phone);
    }

    public async Task<byte[]> GetQrCodeAsync(string instanceId, string token, CancellationToken ct = default)
    {
        var normalizedId = NormalizeInstanceId(instanceId);
        var url = $"{BaseUrl}/{normalizedId}/instance/qrCode?token={token}";
        var response = await http.GetAsync(url, ct);

        var json = await response.Content.ReadAsStringAsync(ct);

        // UltraMsg devuelve JSON:
        // Exito: {"qrCode":"data:image/png;base64,..."} o imagen directa
        // Error: {"error":"instance status is not equal \"qr\""}
        if (!response.IsSuccessStatusCode || json.Contains("\"error\""))
        {
            // Si contiene base64, intentar parsear
            try
            {
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("qrCode", out var qrProp))
                {
                    var qrData = qrProp.GetString();
                    if (qrData != null && qrData.Contains("base64,"))
                    {
                        var base64 = qrData[(qrData.IndexOf("base64,") + 7)..];
                        return Convert.FromBase64String(base64);
                    }
                }
            }
            catch { /* No es JSON valido, devolver error */ }

            throw new InvalidOperationException("QR no disponible: la instancia no esta en estado QR.");
        }

        // Intentar parsear como JSON con qrCode base64
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("qrCode", out var qrProp))
            {
                var qrData = qrProp.GetString();
                if (qrData != null && qrData.Contains("base64,"))
                {
                    var base64 = qrData[(qrData.IndexOf("base64,") + 7)..];
                    return Convert.FromBase64String(base64);
                }
                if (qrData != null)
                {
                    return Convert.FromBase64String(qrData);
                }
            }
        }
        catch (JsonException)
        {
            // No es JSON — probablemente es imagen binaria directa
        }

        // Si no es JSON, asumir que es imagen binaria directa
        return System.Text.Encoding.UTF8.GetBytes(json).Length > 0
            ? await response.Content.ReadAsByteArrayAsync(ct)
            : throw new InvalidOperationException("No se pudo obtener el QR.");
    }

    public async Task<bool> RestartAsync(string instanceId, string token, CancellationToken ct = default)
    {
        var normalizedId = NormalizeInstanceId(instanceId);
        var url = $"{BaseUrl}/{normalizedId}/instance/restart";
        var payload = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
        var response = await http.PostAsync(url, payload, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> LogoutAsync(string instanceId, string token, CancellationToken ct = default)
    {
        var normalizedId = NormalizeInstanceId(instanceId);
        var url = $"{BaseUrl}/{normalizedId}/instance/logout";
        var payload = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
        var response = await http.PostAsync(url, payload, ct);
        return response.IsSuccessStatusCode;
    }
}
