using System.Text.Json;

namespace AgentFlow.Infrastructure.Morosidad;

/// <summary>
/// Extrae valores de un JsonElement usando expresiones de path estilo JsonPath simplificado.
/// Soporta: $.campo, $.padre.hijo, $.padre.hijo[0].campo
/// No soporta wildcards (*), filtros ([?()]) ni descendant (..); para eso se usaría JsonPath.Net.
/// </summary>
public static class FieldMappingExtractor
{
    /// <summary>
    /// Extrae el valor string de un campo en un JsonElement usando un path.
    /// </summary>
    /// <param name="element">El objeto JSON (un ítem del array de morosidad).</param>
    /// <param name="path">Path estilo JsonPath. Ej: "$.telefono", "$.cliente.nombre", "$.polizas[0].numero".</param>
    /// <param name="defaultValue">Valor a devolver si el path no encuentra nada.</param>
    /// <returns>Valor string o defaultValue si no se encuentra.</returns>
    public static string? Extract(JsonElement element, string? path, string? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return defaultValue;

        try
        {
            // Normalizar path: quitar "$." o "$" del inicio
            var normalized = path.Trim();
            if (normalized.StartsWith("$.")) normalized = normalized[2..];
            else if (normalized.StartsWith("$")) normalized = normalized[1..];

            if (string.IsNullOrEmpty(normalized)) return defaultValue;

            var result = NavigatePath(element, normalized);

            return result switch
            {
                null => defaultValue,
                _ => result
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    private static string? NavigatePath(JsonElement element, string path)
    {
        // Separar el primer segmento del resto
        var dotIdx = FindNextDot(path);
        string segment;
        string? remainder;

        if (dotIdx < 0)
        {
            segment = path;
            remainder = null;
        }
        else
        {
            segment = path[..dotIdx];
            remainder = path[(dotIdx + 1)..];
        }

        // Detectar índice de array: campo[0]
        var bracketOpen = segment.IndexOf('[');
        int? arrayIndex = null;

        if (bracketOpen >= 0)
        {
            var bracketClose = segment.IndexOf(']', bracketOpen);
            if (bracketClose > bracketOpen)
            {
                var indexStr = segment[(bracketOpen + 1)..bracketClose];
                if (int.TryParse(indexStr, out var idx)) arrayIndex = idx;
                segment = segment[..bracketOpen];
            }
        }

        // Navegar el campo en el objeto actual
        if (!string.IsNullOrEmpty(segment))
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(segment, out var child)) return null;
            element = child;
        }

        // Navegar índice de array si existe
        if (arrayIndex.HasValue)
        {
            if (element.ValueKind != JsonValueKind.Array) return null;
            var arr = element.EnumerateArray().ToArray();
            if (arrayIndex.Value >= arr.Length) return null;
            element = arr[arrayIndex.Value];
        }

        // Si hay más segmentos, continuar
        if (!string.IsNullOrEmpty(remainder))
            return NavigatePath(element, remainder);

        // Convertir el valor final a string
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Null   => null,
            _                    => element.GetRawText()
        };
    }

    /// <summary>Encuentra el próximo '.' que no esté dentro de corchetes.</summary>
    private static int FindNextDot(string path)
    {
        var depth = 0;
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] == '[') depth++;
            else if (path[i] == ']') depth--;
            else if (path[i] == '.' && depth == 0) return i;
        }
        return -1;
    }
}
