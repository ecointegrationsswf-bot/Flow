using System.Text.Json;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Analiza un JSON de respuesta y detecta los campos disponibles con su dataType inferido.
/// Usado por el WebhookTestController para pre-llenar el OutputSchema en el wizard.
///
/// Heurística de detección:
/// - string que parsea como URL válida → "url"
/// - string largo (>500 chars) con caracteres base64 válidos → "base64"
/// - string que parsea como fecha ISO → "date"
/// - string normal → "string"
/// - number → "number"
/// - true/false → "boolean"
/// - array → "array"
/// - object → "object" (y se recursiona sobre sus props con dot-notation)
/// </summary>
public static class FieldDetector
{
    public static List<DetectedField> Detect(JsonElement root)
    {
        var result = new List<DetectedField>();
        DetectRecursive(root, "", result);
        return result;
    }

    private static void DetectRecursive(JsonElement element, string prefix, List<DetectedField> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                // Si es root (prefix vacío), iterar las props. Si es un objeto anidado, también.
                foreach (var prop in element.EnumerateObject())
                {
                    var newPath = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

                    // Si la prop es un objeto con props propias, recursionar
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        // También registrar el objeto mismo como "object" para que el usuario pueda
                        // optar por enviar el objeto completo o sus hijos
                        result.Add(new DetectedField(newPath, "object"));
                        DetectRecursive(prop.Value, newPath, result);
                    }
                    else
                    {
                        var dataType = InferDataType(prop.Value);
                        result.Add(new DetectedField(newPath, dataType));
                    }
                }
                break;

            case JsonValueKind.Array:
                // Los arrays de nivel raíz también se registran
                if (!string.IsNullOrEmpty(prefix))
                    result.Add(new DetectedField(prefix, "array"));
                break;
        }
    }

    private static string InferDataType(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => InferStringType(value.GetString() ?? ""),
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            _ => "string"
        };
    }

    private static string InferStringType(string value)
    {
        if (string.IsNullOrEmpty(value)) return "string";

        // URL: http:// o https://
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return "url";

        // Date: ISO 8601
        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out _) && (value.Contains('-') || value.Contains('/')))
            return "date";

        // Base64: string largo sin espacios que matchea el charset base64
        if (value.Length > 500 && IsLikelyBase64(value))
            return "base64";

        return "string";
    }

    private static bool IsLikelyBase64(string value)
    {
        // Heurística simple: los caracteres base64 son A-Z, a-z, 0-9, +, /, =
        // Si >95% de los caracteres matchean, lo consideramos base64
        var validCount = value.Count(c =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
            (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=');
        return validCount * 100 / value.Length >= 95;
    }
}

/// <summary>
/// Campo detectado en un JSON de respuesta.
/// Usado para pre-llenar el OutputSchema en el wizard.
/// </summary>
public record DetectedField(string FieldPath, string DataType);
