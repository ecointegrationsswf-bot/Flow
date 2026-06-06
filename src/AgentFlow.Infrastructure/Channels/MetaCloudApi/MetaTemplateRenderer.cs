using System.Text.Json;
using System.Text.RegularExpressions;
using AgentFlow.Domain.Entities;

namespace AgentFlow.Infrastructure.Channels.MetaCloudApi;

/// <summary>
/// Motor de sustitución de plantillas Meta, compartido por el dispatcher de campañas
/// (mensaje inicial / lanzamiento) y el FollowUpSweep (seguimientos). Resuelve los
/// parámetros {{n}} de una plantilla con los datos de un contacto, según el mapeo
/// {{n}}→campo (ParameterMappingJson) y el contexto del contacto (ContactDataJson +
/// campos estándar). No toca la BD ni la red — puro cómputo y testeable.
/// </summary>
public interface IMetaTemplateRenderer
{
    MetaRenderResult Render(MetaMessageTemplate template, CampaignContact contact);
}

/// <param name="BodyParams">Valores de {{1}},{{2}}… en orden, listos para el payload de Meta.</param>
/// <param name="HeaderParam">Valor del header si tuviera 1 variable (Fase actual: null).</param>
/// <param name="RenderedBody">Cuerpo con los valores ya sustituidos (para mostrar en el monitor).</param>
public record MetaRenderResult(
    bool Success, IReadOnlyList<string> BodyParams, string? HeaderParam, string RenderedBody, string? Error)
{
    public static MetaRenderResult Fail(string error) => new(false, [], null, "", error);
}

public partial class MetaTemplateRenderer : IMetaTemplateRenderer
{
    public MetaRenderResult Render(MetaMessageTemplate t, CampaignContact contact)
    {
        var ctx = BuildContactContext(contact);

        var bodyVarCount = CountPlaceholders(t.BodyText);
        var mapping = DeserializeBodyMapping(t.ParameterMappingJson);

        if (mapping.Count < bodyVarCount)
            return MetaRenderResult.Fail(
                $"La plantilla '{t.Name}' tiene {bodyVarCount} variable(s) pero no tiene mapeo de campos. " +
                "Configurá el mapeo de variables (o generala desde el prompt).");

        var bodyParams = new List<string>(bodyVarCount);
        for (var v = 0; v < bodyVarCount; v++)
        {
            var field = mapping[v];
            var value = ctx.GetValueOrDefault(field ?? "");
            if (string.IsNullOrWhiteSpace(value))
                return MetaRenderResult.Fail(
                    $"Dato faltante para la plantilla '{t.Name}': el campo '{field}' (variable {{{{{v + 1}}}}}) " +
                    "está vacío para este contacto. Revisá que el archivo tenga esa columna con valor.");
            bodyParams.Add(value!);
        }

        var rendered = SubstitutePlaceholders(t.BodyText, bodyParams);
        return new MetaRenderResult(true, bodyParams, null, rendered, null);
    }

    /// <summary>
    /// Contexto de sustitución: claves del ContactDataJson (todas las filas, primera gana)
    /// + campos estándar que el motor siempre tiene. Case-insensitive.
    /// </summary>
    public static Dictionary<string, string> BuildContactContext(CampaignContact contact)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(contact.ContactDataJson))
        {
            try
            {
                var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(contact.ContactDataJson);
                if (rows is not null)
                    foreach (var row in rows)
                        foreach (var (k, val) in row)
                        {
                            var s = val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
                            ctx.TryAdd(k, s);
                        }
            }
            catch { /* JSON malformado → seguimos con estándar */ }
        }
        ctx.TryAdd("NombreCliente", contact.ClientName ?? "");
        ctx.TryAdd("NumeroPoliza", contact.PolicyNumber ?? "");
        ctx.TryAdd("MontoDeuda", contact.PendingAmount?.ToString("F2") ?? "");
        ctx.TryAdd("Aseguradora", contact.InsuranceCompany ?? "");
        ctx.TryAdd("Celular", contact.PhoneNumber ?? "");
        ctx.TryAdd("Email", contact.Email ?? "");
        return ctx;
    }

    public static List<string> DeserializeBodyMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("body", out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        }
        catch { }
        return [];
    }

    public static int CountPlaceholders(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var set = new HashSet<int>();
        foreach (Match m in PlaceholderRegex().Matches(text))
            if (int.TryParse(m.Groups[1].Value, out var n)) set.Add(n);
        return set.Count;
    }

    public static string SubstitutePlaceholders(string text, IReadOnlyList<string> values) =>
        PlaceholderRegex().Replace(text, m =>
            int.TryParse(m.Groups[1].Value, out var n) && n >= 1 && n <= values.Count ? values[n - 1] : m.Value);

    [GeneratedRegex(@"\{\{\s*(\d+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
