using System.Text.Json;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoint para probar un webhook desde el Webhook Builder de la UI.
/// Ejecuta un request de prueba con el payload que le da el frontend y
/// devuelve el body de respuesta junto con los campos auto-detectados
/// para pre-llenar el OutputSchema.
/// </summary>
[ApiController]
[Route("api/actions")]
[Authorize]
public class WebhookTestController(IHttpDispatcher httpDispatcher) : ControllerBase
{
    [HttpPost("test-webhook")]
    public async Task<IActionResult> TestWebhook([FromBody] TestWebhookRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.WebhookUrl))
            return BadRequest(new { error = "WebhookUrl es requerido." });

        var endpointConfig = new WebhookEndpointConfig
        {
            WebhookUrl = req.WebhookUrl,
            WebhookMethod = req.WebhookMethod ?? "POST",
            AuthType = req.AuthType ?? "None",
            AuthValue = req.AuthValue,
            ApiKeyHeaderName = req.ApiKeyHeaderName ?? "X-Api-Key",
            WebhookHeaders = req.WebhookHeaders,
            TimeoutSeconds = req.TimeoutSeconds > 0 ? req.TimeoutSeconds : 10,
        };

        // Payload de prueba: si el cliente envía samplePayload lo usamos, sino un objeto vacío
        object payload = req.SamplePayload is not null
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(req.SamplePayload.Value.GetRawText()) ?? new Dictionary<string, object?>()
            : new Dictionary<string, object?>();

        var contentType = req.ContentType ?? "application/json";

        // Ejecutar el HTTP call
        var httpResult = await httpDispatcher.SendAsync(endpointConfig, payload, contentType, ct);

        if (!httpResult.Success)
        {
            return Ok(new TestWebhookResponse
            {
                Success = false,
                HttpStatus = httpResult.StatusCode,
                ErrorMessage = httpResult.ErrorMessage,
                DurationMs = httpResult.DurationMs,
            });
        }

        // Parsear body y detectar campos
        List<DetectedField> detectedFields = [];
        JsonElement? parsedBody = null;

        try
        {
            using var doc = JsonDocument.Parse(httpResult.Body);
            var root = doc.RootElement.Clone();
            parsedBody = root;
            detectedFields = FieldDetector.Detect(root);
        }
        catch
        {
            // Body no es JSON válido — devolvemos el raw y sin campos detectados
        }

        return Ok(new TestWebhookResponse
        {
            Success = true,
            HttpStatus = httpResult.StatusCode,
            ResponseBody = parsedBody,
            RawBody = httpResult.Body,
            DetectedFields = detectedFields.Select(f => new DetectedFieldDto(f.FieldPath, f.DataType)).ToList(),
            DurationMs = httpResult.DurationMs,
        });
    }
}

public record TestWebhookRequest(
    string WebhookUrl,
    string? WebhookMethod = "POST",
    string? ContentType = "application/json",
    string? AuthType = "None",
    string? AuthValue = null,
    string? ApiKeyHeaderName = "X-Api-Key",
    string? WebhookHeaders = null,
    int TimeoutSeconds = 10,
    JsonElement? SamplePayload = null
);

public class TestWebhookResponse
{
    public bool Success { get; set; }
    public int HttpStatus { get; set; }
    public JsonElement? ResponseBody { get; set; }
    public string? RawBody { get; set; }
    public List<DetectedFieldDto>? DetectedFields { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}

public record DetectedFieldDto(string FieldPath, string DataType);
