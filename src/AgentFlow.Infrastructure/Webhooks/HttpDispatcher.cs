using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Webhooks;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Implementación del dispatcher HTTP para webhooks del tenant.
///
/// Responsabilidades:
/// - Serializar el payload según ContentType (JSON, form-urlencoded, query-string).
/// - Aplicar autenticación según AuthType (None, ApiKey, Bearer).
/// - Agregar headers extra del tenant.
/// - Manejar timeout configurable.
/// - Capturar errores de red/timeout/HTTP y traducirlos a mensajes controlados.
///
/// NO loguea credenciales ni bodies con datos sensibles — solo URL y StatusCode.
/// </summary>
public class HttpDispatcher(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpDispatcher> logger) : IHttpDispatcher
{
    public async Task<HttpDispatchResult> SendAsync(
        WebhookEndpointConfig endpointConfig,
        object payload,
        string contentType,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrEmpty(endpointConfig.WebhookUrl))
            return HttpDispatchResult.Fail("WebhookUrl vacío");

        var method = (endpointConfig.WebhookMethod ?? "POST").ToUpper();
        var timeoutSeconds = endpointConfig.TimeoutSeconds > 0 ? endpointConfig.TimeoutSeconds : 10;

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // ── Construir request ──
            HttpRequestMessage request;

            if (method == "GET")
            {
                // GET → payload como query string
                var queryString = BuildQueryString(payload);
                var urlWithQuery = string.IsNullOrEmpty(queryString)
                    ? endpointConfig.WebhookUrl
                    : $"{endpointConfig.WebhookUrl}{(endpointConfig.WebhookUrl.Contains('?') ? "&" : "?")}{queryString}";
                request = new HttpRequestMessage(HttpMethod.Get, urlWithQuery);
            }
            else
            {
                // POST / PUT / PATCH → payload como body
                request = new HttpRequestMessage(ResolveHttpMethod(method), endpointConfig.WebhookUrl);
                request.Content = BuildContent(payload, contentType);
            }

            // ── Autenticación ──
            ApplyAuth(request, endpointConfig);

            // ── Headers extra del tenant ──
            ApplyCustomHeaders(request, endpointConfig.WebhookHeaders);

            // ── Ejecutar ──
            logger.LogInformation("[HttpDispatcher] {Method} {Url} (timeout {Timeout}s)",
                method, endpointConfig.WebhookUrl, timeoutSeconds);

            var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[HttpDispatcher] HTTP {Status} en {Url} — {Duration}ms — body: {Body}",
                    (int)response.StatusCode, endpointConfig.WebhookUrl, sw.ElapsedMilliseconds,
                    body.Length > 500 ? body[..500] + "..." : body);
                return HttpDispatchResult.Fail(
                    $"HTTP {(int)response.StatusCode}",
                    (int)response.StatusCode,
                    sw.ElapsedMilliseconds,
                    body);
            }

            logger.LogInformation("[HttpDispatcher] OK {Status} en {Url} — {Duration}ms",
                (int)response.StatusCode, endpointConfig.WebhookUrl, sw.ElapsedMilliseconds);

            return HttpDispatchResult.Ok((int)response.StatusCode, body, sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            logger.LogWarning("[HttpDispatcher] Timeout en {Url} tras {Timeout}s",
                endpointConfig.WebhookUrl, timeoutSeconds);
            return HttpDispatchResult.Fail(
                $"Timeout tras {timeoutSeconds}s",
                0,
                sw.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            logger.LogWarning("[HttpDispatcher] Error de red en {Url}: {Message}",
                endpointConfig.WebhookUrl, ex.Message);
            return HttpDispatchResult.Fail($"Error de red: {ex.Message}", 0, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[HttpDispatcher] Error inesperado en {Url}", endpointConfig.WebhookUrl);
            return HttpDispatchResult.Fail($"Error: {ex.Message}", 0, sw.ElapsedMilliseconds);
        }
    }

    // ── Helpers ──

    private static HttpMethod ResolveHttpMethod(string method) => method switch
    {
        "POST" => HttpMethod.Post,
        "PUT" => HttpMethod.Put,
        "PATCH" => HttpMethod.Patch,
        _ => HttpMethod.Post
    };

    private static HttpContent BuildContent(object payload, string contentType)
    {
        var normalizedContentType = (contentType ?? "application/json").ToLower();

        if (normalizedContentType.Contains("x-www-form-urlencoded"))
        {
            // Aplanar el payload a key=value para form-urlencoded
            var formData = FlattenForForm(payload);
            return new FormUrlEncodedContent(formData);
        }

        if (normalizedContentType.Contains("multipart/form-data"))
        {
            var multipart = new MultipartFormDataContent();
            foreach (var (key, value) in FlattenForForm(payload))
                multipart.Add(new StringContent(value ?? ""), key);
            return multipart;
        }

        // Default: application/json
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string BuildQueryString(object payload)
    {
        var parts = FlattenForForm(payload)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}");
        return string.Join("&", parts);
    }

    /// <summary>
    /// Aplana un payload (plano o anidado) a pares key=string para form-urlencoded y query strings.
    /// Para objetos anidados usa dot-notation: { a: { b: 1 } } → "a.b=1".
    /// </summary>
    private static List<KeyValuePair<string, string?>> FlattenForForm(object payload)
    {
        var result = new List<KeyValuePair<string, string?>>();
        FlattenRecursive(payload, "", result);
        return result;
    }

    private static void FlattenRecursive(object? value, string prefix, List<KeyValuePair<string, string?>> result)
    {
        if (value is null)
        {
            if (!string.IsNullOrEmpty(prefix))
                result.Add(new KeyValuePair<string, string?>(prefix, null));
            return;
        }

        if (value is IDictionary<string, object?> dict)
        {
            foreach (var (k, v) in dict)
            {
                var newKey = string.IsNullOrEmpty(prefix) ? k : $"{prefix}.{k}";
                FlattenRecursive(v, newKey, result);
            }
            return;
        }

        if (value is IDictionary<string, string?> dictString)
        {
            foreach (var (k, v) in dictString)
            {
                var newKey = string.IsNullOrEmpty(prefix) ? k : $"{prefix}.{k}";
                result.Add(new KeyValuePair<string, string?>(newKey, v));
            }
            return;
        }

        // Primitivo
        result.Add(new KeyValuePair<string, string?>(prefix, value.ToString()));
    }

    private static void ApplyAuth(HttpRequestMessage request, WebhookEndpointConfig config)
    {
        if (string.IsNullOrEmpty(config.AuthValue)) return;

        switch (config.AuthType?.ToLower())
        {
            case "bearer":
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AuthValue);
                break;

            case "apikey":
                var headerName = string.IsNullOrEmpty(config.ApiKeyHeaderName) ? "X-Api-Key" : config.ApiKeyHeaderName;
                request.Headers.TryAddWithoutValidation(headerName, config.AuthValue);
                break;

            // "None" o cualquier otro → no agregar header
        }
    }

    private static void ApplyCustomHeaders(HttpRequestMessage request, string? headersJson)
    {
        if (string.IsNullOrEmpty(headersJson)) return;

        try
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            if (headers is null) return;

            foreach (var (key, value) in headers)
            {
                // No sobreescribir Authorization si ya existe
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && request.Headers.Authorization is not null)
                    continue;
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }
        catch
        {
            // Headers JSON inválido — silencioso
        }
    }
}
