using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Persistence;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.AI;

/// <summary>
/// Genera borradores de plantilla de Meta a partir del PROMPT del maestro de campaña.
///
/// El prompt del maestro son INSTRUCCIONES para que la IA redacte mensajes (no un texto
/// literal). Acá usamos la IA para "destilar" ese prompt en el/los mensaje(s) INICIAL(es)
/// que el agente enviaría, ya en formato de plantilla de Meta:
///   • texto fijo en español, tono del prompt,
///   • variables numeradas {{1}},{{2}}… SOLO en las partes que cambian por cliente,
///   • una plantilla por burbuja si el prompt separa con '~' (igual que UltraMsg),
///   • cada {{n}} mapeada a un campo disponible (estándar + columnas del Excel).
///
/// Usa la LlmApiKey del tenant (cliente), igual que InitialMessageGenerator.
/// El resultado son BORRADORES — el usuario revisa antes de enviarlos a Meta.
/// </summary>
public interface IMetaTemplateGenerator
{
    /// <param name="providedColumns">
    /// Columnas reales del Excel que el usuario subió (cuando el maestro NO tiene proceso
    /// de descarga). Null/vacío si no se subió nada.
    /// </param>
    /// <param name="providedSampleJson">Sample del Excel subido (para ejemplos del LLM).</param>
    Task<MetaTemplateGenResult> GenerateAsync(
        Guid tenantId, Guid campaignTemplateId,
        IReadOnlyList<string>? providedColumns, string? providedSampleJson,
        CancellationToken ct = default);

    /// <summary>
    /// Campos disponibles para mapear {{n}} en el formulario manual: estándar +
    /// columnas del proceso de descarga (ActionFieldMapping) o del Excel de ejemplo
    /// (SampleDataJson). Nunca bloquea — al menos devuelve los estándar.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableFieldsAsync(
        Guid tenantId, Guid campaignTemplateId, CancellationToken ct = default);
}

public record GeneratedVar(int Placeholder, string Field, string Sample);
public record GeneratedTemplate(string Body, string? FooterText, List<GeneratedVar> Variables);

/// <param name="NeedsStructure">
/// true cuando no se pudo determinar la estructura de datos (sin proceso de descarga,
/// sin Excel subido y sin datos de ejemplo). El frontend debe pedir el Excel y reintentar.
/// </param>
public record MetaTemplateGenResult(bool Success, List<GeneratedTemplate> Templates, string? Error, bool NeedsStructure = false)
{
    public static MetaTemplateGenResult Ok(List<GeneratedTemplate> t) => new(true, t, null);
    public static MetaTemplateGenResult Fail(string error) => new(false, [], error);
    public static MetaTemplateGenResult Structure() => new(false, [],
        "Necesito la estructura de datos para mapear las variables. Subí el Excel que vas a usar en la campaña.", true);
}

public class MetaTemplateGenerator(
    AgentFlowDbContext db,
    ILogger<MetaTemplateGenerator> logger) : IMetaTemplateGenerator
{
    // Campos estándar que SIEMPRE están disponibles (los puebla InitialMessageGenerator).
    private static readonly string[] StandardFields =
        ["NombreCliente", "MontoDeuda", "Aseguradora", "NumeroPoliza", "Celular", "Email"];

    public async Task<MetaTemplateGenResult> GenerateAsync(
        Guid tenantId, Guid campaignTemplateId,
        IReadOnlyList<string>? providedColumns, string? providedSampleJson,
        CancellationToken ct = default)
    {
        var maestro = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == campaignTemplateId && t.TenantId == tenantId, ct);
        if (maestro is null)
            return MetaTemplateGenResult.Fail("Maestro de campaña no encontrado para este tenant.");
        if (string.IsNullOrWhiteSpace(maestro.SystemPrompt))
            return MetaTemplateGenResult.Fail("El maestro no tiene prompt definido. Configurá el prompt primero.");

        var apiKey = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.LlmApiKey).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return MetaTemplateGenResult.Fail("El tenant no tiene API key de IA configurada.");

        // ── Resolver la ESTRUCTURA REAL de datos que alimentará la campaña ──────
        // Prioridad: (1) proceso de descarga atado al maestro → sus ActionFieldMapping;
        // (2) Excel subido por el usuario (providedColumns); (3) datos de ejemplo del
        // maestro (SampleDataJson). Si no hay nada → NeedsStructure (el front pide el Excel).
        var realColumns = new List<string>();
        string? sampleForLlm;

        var delinqConfig = await db.Set<ActionDelinquencyConfig>()
            .Where(c => c.CampaignTemplateId == campaignTemplateId && c.TenantId == tenantId && c.IsActive)
            .FirstOrDefaultAsync(ct);

        if (delinqConfig is not null)
        {
            // Proceso de descarga: las claves del ContactDataJson son los ColumnKey del mapeo.
            realColumns = await db.Set<ActionFieldMapping>()
                .Where(m => m.ActionDefinitionId == delinqConfig.ActionDefinitionId && m.IsEnabled)
                .OrderBy(m => m.SortOrder)
                .Select(m => m.ColumnKey)
                .ToListAsync(ct);
            realColumns = realColumns.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
            sampleForLlm = maestro.SampleDataJson;
            logger.LogInformation("[MetaTemplateGen] Maestro {Id}: estructura desde proceso de descarga ({N} columnas).", campaignTemplateId, realColumns.Count);
        }
        else if (providedColumns is { Count: > 0 })
        {
            realColumns = providedColumns.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            sampleForLlm = providedSampleJson;
            logger.LogInformation("[MetaTemplateGen] Maestro {Id}: estructura desde Excel subido ({N} columnas).", campaignTemplateId, realColumns.Count);
        }
        else
        {
            var sampleCols = ExtractColumns(maestro.SampleDataJson).ToList();
            if (sampleCols.Count == 0)
                return MetaTemplateGenResult.Structure();
            realColumns = sampleCols;
            sampleForLlm = maestro.SampleDataJson;
            logger.LogInformation("[MetaTemplateGen] Maestro {Id}: estructura desde datos de ejemplo ({N} columnas).", campaignTemplateId, realColumns.Count);
        }

        // Campos disponibles para el mapeo = columnas reales (prioridad) + estándar
        // (que InitialMessageGenerator siempre inyecta al enviar: NombreCliente, MontoDeuda…).
        var fields = new List<string>(realColumns);
        foreach (var s in StandardFields)
            if (!fields.Contains(s, StringComparer.OrdinalIgnoreCase)) fields.Add(s);

        var system = BuildSystemPrompt(fields);
        var user = BuildUserPrompt(maestro.SystemPrompt, sampleForLlm);

        string raw;
        try
        {
            var client = new AnthropicClient(apiKey);
            var resp = await client.Messages.GetClaudeMessageAsync(new MessageParameters
            {
                Model = "claude-haiku-4-5-20251001",
                MaxTokens = 1500,
                System = [new SystemMessage(system)],
                Messages = [new() { Role = RoleType.User, Content = [new TextContent { Text = user }] }],
                Stream = false,
                Temperature = new decimal(0.2),
            }, ct);
            raw = resp.Content.OfType<TextContent>().FirstOrDefault()?.Text?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MetaTemplateGen] Error llamando a Claude para maestro {Id}.", campaignTemplateId);
            return MetaTemplateGenResult.Fail($"Error de IA: {ex.Message}");
        }

        return ParseResponse(raw, fields);
    }

    public async Task<IReadOnlyList<string>> GetAvailableFieldsAsync(
        Guid tenantId, Guid campaignTemplateId, CancellationToken ct = default)
    {
        var maestro = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == campaignTemplateId && t.TenantId == tenantId, ct);

        var fields = new List<string>();

        if (maestro is not null)
        {
            var delinqConfig = await db.Set<ActionDelinquencyConfig>()
                .Where(c => c.CampaignTemplateId == campaignTemplateId && c.TenantId == tenantId && c.IsActive)
                .FirstOrDefaultAsync(ct);

            if (delinqConfig is not null)
            {
                var cols = await db.Set<ActionFieldMapping>()
                    .Where(m => m.ActionDefinitionId == delinqConfig.ActionDefinitionId && m.IsEnabled)
                    .OrderBy(m => m.SortOrder)
                    .Select(m => m.ColumnKey)
                    .ToListAsync(ct);
                fields.AddRange(cols.Where(c => !string.IsNullOrWhiteSpace(c)));
            }
            else
            {
                fields.AddRange(ExtractColumns(maestro.SampleDataJson));
            }
        }

        // Estándar siempre disponibles.
        foreach (var s in StandardFields)
            if (!fields.Contains(s, StringComparer.OrdinalIgnoreCase)) fields.Add(s);

        return fields.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildSystemPrompt(List<string> fields)
    {
        var fieldList = string.Join(", ", fields);
        return ("""
Eres un experto en plantillas de mensajes de WhatsApp de Meta (HSM) para cobros de seguros en Panamá.
Recibirás las INSTRUCCIONES de un agente de IA (su prompt) y debes producir el/los MENSAJE(S)
INICIAL(ES) que ese agente enviaría a un cliente, ya convertidos a formato de PLANTILLA de Meta.

Reglas estrictas:
- Texto fijo, claro, profesional y en español. Respetá el tono y la intención del prompt.
- Usá variables numeradas {{1}}, {{2}}, {{3}}… SOLO en las partes que cambian por cliente
  (nombre, saldo, póliza, aseguradora, etc.). El resto es texto fijo.
- La numeración EMPIEZA en {{1}} en CADA mensaje (cada burbuja es una plantilla independiente).
- Si el prompt indica separar el mensaje en varias burbujas (suele usar el carácter '~'),
  devolvé VARIOS mensajes: uno por burbuja.
- NUNCA incluyas el carácter '~' dentro del texto: cada elemento del array YA es una
  burbuja separada, el '~' no debe aparecer en el "body" ni en el "footer".
- Cada variable debe mapear a UNO de estos campos disponibles (no inventes otros):
  __FIELDS__.
- NUNCA uses variables con nombre como {{NombreCliente}}. SOLO numéricas {{1}}, {{2}}.
- No incluyas saludos tipo "[INTENT:...]" ni etiquetas internas del agente.
- Para cada variable da un valor de EJEMPLO realista (Meta lo exige para revisar).

Devolvé EXCLUSIVAMENTE un JSON válido (sin texto extra, sin ```), con esta forma:
{
  "templates": [
    {
      "body": "Hola {{1}}, le recordamos que su póliza {{2}} con {{3}} tiene un saldo pendiente.",
      "footer": "JAM Consulting",
      "variables": [
        { "placeholder": 1, "field": "NombreCliente", "sample": "Juan Pérez" },
        { "placeholder": 2, "field": "NumeroPoliza", "sample": "POL-12345" },
        { "placeholder": 3, "field": "Aseguradora", "sample": "ASSA" }
      ]
    }
  ]
}
El campo "footer" es opcional. Si no aplica, omitilo o ponelo en null.
""").Replace("__FIELDS__", fieldList);
    }

    private static string BuildUserPrompt(string prompt, string? sampleDataJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("INSTRUCCIONES DEL AGENTE (prompt del maestro):");
        sb.AppendLine(prompt);
        if (!string.IsNullOrWhiteSpace(sampleDataJson))
        {
            sb.AppendLine();
            sb.AppendLine("DATOS DE EJEMPLO (para inferir qué variables existen):");
            sb.AppendLine(sampleDataJson);
        }
        sb.AppendLine();
        sb.AppendLine("Generá la(s) plantilla(s) según las reglas. Devolvé solo el JSON.");
        return sb.ToString();
    }

    private MetaTemplateGenResult ParseResponse(string raw, List<string> validFields)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return MetaTemplateGenResult.Fail("La IA devolvió una respuesta vacía.");

        // Quitar fences ```json … ``` si los hubiera.
        var json = StripFences(raw);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("templates", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                return MetaTemplateGenResult.Fail("La IA no devolvió plantillas. Probá de nuevo o revisá el prompt.");

            var templates = new List<GeneratedTemplate>();
            foreach (var t in arr.EnumerateArray())
            {
                var body = CleanBubbleSeparators(t.TryGetProperty("body", out var b) ? b.GetString() : "");
                if (string.IsNullOrWhiteSpace(body)) continue;
                var footer = CleanBubbleSeparators(t.TryGetProperty("footer", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null);
                if (string.IsNullOrWhiteSpace(footer)) footer = null;

                var vars = new List<GeneratedVar>();
                if (t.TryGetProperty("variables", out var vArr) && vArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in vArr.EnumerateArray())
                    {
                        var ph = v.TryGetProperty("placeholder", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
                        var field = v.TryGetProperty("field", out var fl) ? fl.GetString() ?? "" : "";
                        var sample = v.TryGetProperty("sample", out var sm) ? sm.GetString() ?? "" : "";
                        // Validar que el campo esté permitido (si no, lo dejamos igual pero queda visible al usuario).
                        if (ph > 0 && !string.IsNullOrWhiteSpace(field))
                            vars.Add(new GeneratedVar(ph, field, sample));
                    }
                }
                templates.Add(new GeneratedTemplate(body, string.IsNullOrWhiteSpace(footer) ? null : footer, vars.OrderBy(x => x.Placeholder).ToList()));
            }

            if (templates.Count == 0)
                return MetaTemplateGenResult.Fail("La IA no produjo plantillas válidas. Probá de nuevo.");

            return MetaTemplateGenResult.Ok(templates);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[MetaTemplateGen] No se pudo parsear el JSON de la IA: {Raw}", Truncate(raw, 500));
            return MetaTemplateGenResult.Fail("La IA devolvió un formato inesperado. Probá de nuevo.");
        }
    }

    /// <summary>
    /// Quita el separador de burbujas '~' del texto: cada plantilla ya es una burbuja
    /// independiente, así que un '~' sería texto literal indeseado (Meta lo mostraría
    /// o lo rechazaría). Reemplaza '~' (con espacios alrededor) por un solo espacio y
    /// normaliza. No toca otros caracteres.
    /// </summary>
    private static string? CleanBubbleSeparators(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var cleaned = Regex.Replace(s, @"[ \t]*~+[ \t]*", " ");   // '~' inline → espacio
        cleaned = Regex.Replace(cleaned, @"\s*~+\s*$", "");        // '~' al final → fuera
        cleaned = Regex.Replace(cleaned, @"[ \t]{2,}", " ");        // colapsar dobles espacios
        return cleaned.Trim();
    }

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            var firstNl = s.IndexOf('\n');
            if (firstNl > 0) s = s[(firstNl + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        // Si hay texto antes/después, recortar al primer '{' y último '}'.
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return (start >= 0 && end > start) ? s[start..(end + 1)] : s;
    }

    private static IEnumerable<string> ExtractColumns(string? sampleDataJson)
    {
        if (string.IsNullOrWhiteSpace(sampleDataJson)) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<Dictionary<string, JsonElement>>? rows = null;
        try { rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(sampleDataJson); } catch { }
        if (rows is null) yield break;
        foreach (var row in rows)
            foreach (var k in row.Keys)
                if (seen.Add(k)) yield return k;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
