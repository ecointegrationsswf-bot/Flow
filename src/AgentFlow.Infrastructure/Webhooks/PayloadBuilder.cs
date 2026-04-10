using System.Globalization;
using AgentFlow.Domain.Webhooks;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Construye el payload del webhook según el InputSchema del tenant.
///
/// Flujo:
///   1. Iterar InputFields y resolver el valor según SourceType:
///      - system       → SystemContext[sourceKey]
///      - conversation → CollectedParams[sourceKey]
///      - static       → StaticValue
///   2. Aplicar cast al DataType declarado (string/number/boolean/date/array).
///   3. Si Structure = nested, expandir dot-notation a objeto anidado.
///      Si Structure = flat, dejar el diccionario plano.
///
/// El resultado es un Dictionary&lt;string, object?&gt; (flat) o un objeto anidado.
/// El HttpDispatcher lo serializará según el ContentType (JSON/form/querystring).
/// </summary>
public class PayloadBuilder : IPayloadBuilder
{
    public object Build(InputSchema schema, CollectedParams collected, SystemContext systemContext)
    {
        var flat = new Dictionary<string, object?>();

        foreach (var field in schema.Fields)
        {
            var rawValue = ResolveSource(field, collected, systemContext);

            // Aplicar default si el valor es null y no es required
            if (rawValue is null && !field.Required && field.DefaultValue is not null)
                rawValue = field.DefaultValue;

            // Cast al tipo declarado
            var typedValue = CastToDataType(rawValue, field.DataType);

            flat[field.FieldPath] = typedValue;
        }

        // Si la estructura es nested, expandir dot-notation a objeto anidado
        return schema.Structure?.ToLower() == "nested"
            ? ExpandToNested(flat)
            : flat;
    }

    /// <summary>
    /// Resuelve el valor crudo del campo según su SourceType.
    /// </summary>
    private static object? ResolveSource(InputField field, CollectedParams collected, SystemContext systemContext)
    {
        return field.SourceType?.ToLower() switch
        {
            "system" => !string.IsNullOrEmpty(field.SourceKey)
                ? systemContext.Get(field.SourceKey)
                : null,

            "conversation" => !string.IsNullOrEmpty(field.SourceKey)
                ? collected.Values.GetValueOrDefault(field.SourceKey)
                : null,

            "static" => field.StaticValue,

            _ => null
        };
    }

    /// <summary>
    /// Convierte el valor crudo al tipo declarado en el InputSchema.
    /// Los errores de cast devuelven el valor original como string.
    /// </summary>
    private static object? CastToDataType(object? raw, string dataType)
    {
        if (raw is null) return null;

        var stringValue = raw.ToString() ?? "";
        if (string.IsNullOrEmpty(stringValue)) return null;

        return dataType?.ToLower() switch
        {
            "number" => decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                ? n
                : (object)stringValue,

            "boolean" => bool.TryParse(stringValue, out var b)
                ? b
                : stringValue.Equals("1") || stringValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                    ? true
                    : stringValue.Equals("0") || stringValue.Equals("false", StringComparison.OrdinalIgnoreCase)
                        ? false
                        : (object)stringValue,

            "date" => DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
                ? d.ToString("O")
                : (object)stringValue,

            "array" => TryDeserializeArray(stringValue) ?? (object)stringValue,

            _ => stringValue
        };
    }

    private static object? TryDeserializeArray(string raw)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<object>>(raw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convierte un diccionario con dot-notation en un objeto anidado.
    /// Ej: {"cliente.cedula": "V-123", "cliente.nombre": "Juan", "pago.monto": 150}
    ///     → {"cliente": {"cedula":"V-123","nombre":"Juan"}, "pago": {"monto":150}}
    /// </summary>
    private static Dictionary<string, object?> ExpandToNested(Dictionary<string, object?> flat)
    {
        var root = new Dictionary<string, object?>();

        foreach (var (path, value) in flat)
        {
            if (!path.Contains('.'))
            {
                // Campo de nivel raíz
                root[path] = value;
                continue;
            }

            var parts = path.Split('.');
            Dictionary<string, object?> cursor = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var key = parts[i];
                if (!cursor.TryGetValue(key, out var existing) || existing is not Dictionary<string, object?> next)
                {
                    next = new Dictionary<string, object?>();
                    cursor[key] = next;
                }
                cursor = next;
            }

            cursor[parts[^1]] = value;
        }

        return root;
    }
}
