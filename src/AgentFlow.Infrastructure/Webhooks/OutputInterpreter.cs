using System.Text.Json;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Procesa la respuesta del webhook según el OutputSchema del tenant.
/// Ejecuta cada outputAction sin lógica hardcodeada por tipo de acción.
///
/// outputActions soportados:
/// - send_to_agent       → acumula en DataForAgent
/// - send_whatsapp_media → FASE 3: solo loguea el evento. TODO: conectar con método existente de envío
/// - inject_context      → guarda en SessionState.ActionContext para turnos futuros
/// - log_only            → Serilog INFO
/// - trigger_escalation  → marca ShouldEscalate si valor falsy
/// </summary>
public class OutputInterpreter(
    ISessionStore sessionStore,
    ILogger<OutputInterpreter> logger) : IOutputInterpreter
{
    public async Task<ActionResult> InterpretAsync(
        string responseBody,
        OutputSchema schema,
        OutputContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            logger.LogWarning("[OutputInterpreter] Body vacío para acción {Action}", context.ActionName);
            return ActionResult.Ok();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(responseBody);
        }
        catch (Exception ex)
        {
            logger.LogWarning("[OutputInterpreter] Body inválido JSON: {Message}", ex.Message);
            return ActionResult.Fail("No pude interpretar la respuesta del servicio.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var agentParts = new List<string>();
            var shouldEscalate = false;

            foreach (var field in schema.Fields)
            {
                var raw = ResolveDotPath(root, field.FieldPath);

                if (raw is null)
                {
                    if (field.Required)
                        logger.LogWarning("[OutputInterpreter] Campo requerido '{Field}' ausente en respuesta de {Action}",
                            field.FieldPath, context.ActionName);
                    continue;
                }

                try
                {
                    switch (field.OutputAction?.ToLower())
                    {
                        case "send_to_agent":
                            var value = FormatForAgent(raw, field.DataType);
                            if (!string.IsNullOrEmpty(value))
                                agentParts.Add($"{field.Label}: {value}");
                            break;

                        case "send_whatsapp_media":
                            HandleWhatsAppMedia(raw, field, context, agentParts);
                            break;

                        case "inject_context":
                            await InjectContextAsync(raw, field, context, ct);
                            break;

                        case "log_only":
                            logger.LogInformation("[ActionLog] {Action}/{Label}: {Value}",
                                context.ActionName, field.Label, FormatForAgent(raw, field.DataType));
                            break;

                        case "trigger_escalation":
                            if (IsFalsy(raw))
                            {
                                logger.LogInformation("[OutputInterpreter] Trigger escalation: {Label}", field.Label);
                                shouldEscalate = true;
                            }
                            break;

                        default:
                            logger.LogWarning("[OutputInterpreter] outputAction desconocido: {Action}", field.OutputAction);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[OutputInterpreter] Error procesando campo '{Field}' de {Action}",
                        field.FieldPath, context.ActionName);
                }
            }

            return ActionResult.Ok(
                dataForAgent: agentParts.Count > 0 ? string.Join(" | ", agentParts) : null,
                shouldEscalate: shouldEscalate);
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Resuelve un path dot-notation en el JSON. Ej: "documento.archivo" → json.documento.archivo.
    /// Devuelve null si el path no existe.
    /// </summary>
    private static JsonElement? ResolveDotPath(JsonElement root, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var parts = path.Split('.');
        var current = root;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(part, out var next)) return null;
            current = next;
        }

        return current;
    }

    /// <summary>
    /// Formatea el JsonElement al tipo indicado para mostrarlo al agente.
    /// </summary>
    private static string FormatForAgent(JsonElement? raw, string dataType)
    {
        if (raw is null) return "";

        var element = raw.Value;

        return dataType?.ToLower() switch
        {
            "string" or "url" or "base64" => element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? ""
                : element.ToString(),

            "number" => element.ValueKind == JsonValueKind.Number
                ? element.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture)
                : element.ToString(),

            "boolean" => element.ValueKind == JsonValueKind.True ? "true" :
                         element.ValueKind == JsonValueKind.False ? "false" : element.ToString(),

            "date" => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.ToString(),

            "array" or "object" => element.GetRawText(),

            _ => element.ToString()
        };
    }

    /// <summary>
    /// FASE 3: Solo loguea el evento y agrega texto al DataForAgent.
    /// TODO: conectar con el método existente de envío de documentos cuando se decida.
    /// </summary>
    private void HandleWhatsAppMedia(
        JsonElement? raw,
        OutputField field,
        OutputContext context,
        List<string> agentParts)
    {
        if (raw is null || string.IsNullOrEmpty(field.MimeType))
        {
            logger.LogWarning("[OutputInterpreter] send_whatsapp_media sin base64 o mimeType para {Action}",
                context.ActionName);
            return;
        }

        try
        {
            var base64 = raw.Value.ValueKind == JsonValueKind.String
                ? raw.Value.GetString() ?? ""
                : raw.Value.GetRawText();

            var bytes = Convert.FromBase64String(base64);

            logger.LogInformation(
                "[OutputInterpreter] send_whatsapp_media: action={Action} label={Label} mimeType={MimeType} size={Size}B to={Phone}",
                context.ActionName, field.Label, field.MimeType, bytes.Length, context.ContactPhone);

            // TODO: Integrar con el método existente de envío de documentos por WhatsApp.
            // Por ahora solo agregamos texto al DataForAgent.
            agentParts.Add($"{field.Label} (documento {field.MimeType}, {bytes.Length} bytes) listo para enviar");
        }
        catch (FormatException)
        {
            logger.LogWarning("[OutputInterpreter] Base64 corrupto en {Field} para {Action}",
                field.FieldPath, context.ActionName);
            agentParts.Add($"Error generando {field.Label}");
        }
    }

    /// <summary>
    /// Guarda el valor en SessionState.ActionContext para que esté disponible en turnos futuros.
    /// </summary>
    private async Task InjectContextAsync(
        JsonElement? raw,
        OutputField field,
        OutputContext context,
        CancellationToken ct)
    {
        if (raw is null || !context.ConversationId.HasValue) return;

        var valueStr = FormatForAgent(raw, field.DataType);
        if (string.IsNullOrEmpty(valueStr)) return;

        try
        {
            var session = await sessionStore.GetAsync(context.TenantId, context.ContactPhone, ct);
            if (session is null) return;

            var newContext = session.ActionContext is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(session.ActionContext);

            newContext[field.FieldPath] = valueStr;

            var updated = session with { ActionContext = newContext };
            await sessionStore.SetAsync(context.TenantId, context.ContactPhone, updated, TimeSpan.FromHours(72), ct);

            logger.LogInformation("[OutputInterpreter] inject_context: {Field} → session Redis", field.FieldPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[OutputInterpreter] Error guardando ActionContext para {Field}", field.FieldPath);
        }
    }

    /// <summary>
    /// Evalúa si un valor es "falsy" para trigger_escalation.
    /// falsy: false, 0, null, "", "false", "0", "null"
    /// </summary>
    private static bool IsFalsy(JsonElement? raw)
    {
        if (raw is null) return true;

        var element = raw.Value;

        return element.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.False => true,
            JsonValueKind.True => false,
            JsonValueKind.Number => element.GetDecimal() == 0,
            JsonValueKind.String => element.GetString() is null or "" or "0" or "false" or "null",
            _ => false
        };
    }
}
