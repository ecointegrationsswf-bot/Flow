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

            // Cast al tipo declarado (Format solo aplica a date)
            var typedValue = CastToDataType(rawValue, field.DataType, field.Format);

            flat[field.FieldPath] = typedValue;
        }

        // rootArray: el body es un ARRAY raíz de objetos — ej. [{"IdPoliza":1},{"IdPoliza":2}].
        // Para endpoints batch (jun 2026: descarga de PDFs de varias pólizas). El valor de un
        // field puede venir como CSV ("1,2,3", típico de un [PARAM:...] del LLM) o como array
        // JSON; cada ítem genera un elemento del array con ese field. Los fields de un solo
        // valor se repiten como constantes en todos los elementos.
        if (schema.Structure?.ToLower() == "rootarray")
            return ExpandToRootArray(schema, collected, systemContext);

        // Si la estructura es nested, expandir dot-notation a objeto anidado
        return schema.Structure?.ToLower() == "nested"
            ? ExpandToNested(flat)
            : flat;
    }

    /// <summary>
    /// Construye el array raíz para Structure=rootArray. Cada field se resuelve igual que
    /// en flat, pero los valores multi-ítem (CSV o JSON array) se expanden: el elemento i
    /// del array recibe el ítem i. El largo del array = máximo de ítems entre los fields.
    /// </summary>
    private List<Dictionary<string, object?>> ExpandToRootArray(
        InputSchema schema, CollectedParams collected, SystemContext systemContext)
    {
        var perField = new List<(InputField Field, List<object?> Items)>();
        var count = 1;

        foreach (var field in schema.Fields)
        {
            var rawValue = ResolveSource(field, collected, systemContext);
            if (rawValue is null && !field.Required && field.DefaultValue is not null)
                rawValue = field.DefaultValue;

            var items = SplitMultiValue(rawValue)
                .Select(v => CastToDataType(v, field.DataType, field.Format))
                .ToList();
            if (items.Count == 0) items.Add(null);

            perField.Add((field, items));
            count = Math.Max(count, items.Count);
        }

        var result = new List<Dictionary<string, object?>>(count);
        for (var i = 0; i < count; i++)
        {
            var element = new Dictionary<string, object?>();
            foreach (var (field, items) in perField)
                element[field.FieldPath] = items.Count > i ? items[i] : items[^1]; // constantes se repiten
            result.Add(element);
        }
        return result;
    }

    /// <summary>
    /// Divide un valor en sus ítems: array JSON ("[1,2]"), CSV ("1,2,3") o valor único.
    /// </summary>
    private static List<object?> SplitMultiValue(object? raw)
    {
        if (raw is null) return [];
        var s = raw.ToString()?.Trim() ?? "";
        if (s.Length == 0) return [];

        if (s.StartsWith('['))
        {
            try
            {
                var arr = System.Text.Json.JsonSerializer.Deserialize<List<object?>>(s);
                if (arr is not null) return arr.Select(x => (object?)x?.ToString()).ToList();
            }
            catch { /* cae al CSV/único */ }
        }

        return s.Contains(',')
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => (object?)x).ToList()
            : [s];
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

            // labelingResult — los campos del JSON producido por ConversationLabelingJob
            // se aplanan en SystemContext con prefijo "result." (ver SystemContextBuilder).
            // El usuario tipea solo el nombre corto (ej: "comentario") y aquí lo prefijamos.
            "labelingresult" => !string.IsNullOrEmpty(field.SourceKey)
                ? systemContext.Get(field.SourceKey.StartsWith("result.")
                    ? field.SourceKey
                    : $"result.{field.SourceKey}")
                : null,

            // lastActionResult — datos del JSON response del eslabón anterior del chain.
            // El handler (ProcessIncomingMessageCommand) aplana ese JSON en SystemContext
            // con prefijo "lastActionResult." antes de ejecutar la acción encadenada.
            // El usuario tipea el nombre corto (ej: "correoDestino") y aquí prefijamos.
            "lastactionresult" => !string.IsNullOrEmpty(field.SourceKey)
                ? systemContext.Get(field.SourceKey.StartsWith("lastActionResult.")
                    ? field.SourceKey
                    : $"lastActionResult.{field.SourceKey}")
                : null,

            // Para conversation: intentar sourceKey primero, luego fieldPath como fallback.
            // El agent emite [PARAM:fieldPath=valor], pero el schema puede tener un sourceKey diferente.
            "conversation" => !string.IsNullOrEmpty(field.SourceKey)
                ? collected.Values.GetValueOrDefault(field.SourceKey)
                  ?? collected.Values.GetValueOrDefault(field.FieldPath)
                : collected.Values.GetValueOrDefault(field.FieldPath),

            "static" => field.StaticValue,

            _ => null
        };
    }

    /// <summary>
    /// Convierte el valor crudo al tipo declarado en el InputSchema.
    /// Los errores de cast devuelven el valor original como string.
    /// </summary>
    private static object? CastToDataType(object? raw, string dataType, string? format = null)
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

            // ISO 8601 corto sin fracción ni TZ ("yyyy-MM-ddTHH:mm:ss"). Universal-friendly
            // y aceptado por CONVERT(DATETIME,...) de SQL Server, que rechaza la versión
            // "O" (7 dígitos de fracción + offset).
            "date" => DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
                ? d.ToString(string.IsNullOrWhiteSpace(format) ? "s" : format, CultureInfo.InvariantCulture)
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
