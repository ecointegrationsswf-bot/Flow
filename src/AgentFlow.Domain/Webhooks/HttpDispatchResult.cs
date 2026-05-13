namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Resultado crudo del HTTP call al webhook del tenant.
/// El OutputInterpreter lo usa luego para construir el ActionResult final.
/// </summary>
public class HttpDispatchResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }

    /// <summary>Body crudo de la respuesta. String vacío si no hay body.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Mensaje de error cuando Success=false (timeout, red, status != 2xx).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Duración de la llamada en milisegundos (para audit log).</summary>
    public long DurationMs { get; init; }

    public static HttpDispatchResult Ok(int statusCode, string body, long durationMs) =>
        new() { Success = true, StatusCode = statusCode, Body = body, DurationMs = durationMs };

    public static HttpDispatchResult Fail(string errorMessage, int statusCode = 0, long durationMs = 0, string body = "") =>
        new() { Success = false, StatusCode = statusCode, ErrorMessage = errorMessage, DurationMs = durationMs, Body = body };
}
