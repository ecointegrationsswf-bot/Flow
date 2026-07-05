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
    IChannelProviderFactory channelFactory,
    Storage.IBlobStorageService blobStorage,
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
            var mediaUrls = new List<string>();
            var shouldEscalate = false;

            foreach (var field in schema.Fields)
            {
                // Wildcard `[*]` (jun 2026): un fieldPath como "polizas[*].documentos[*].base64"
                // resuelve TODAS las coincidencias y la outputAction se ejecuta para cada una
                // (ej: enviar N PDFs por WhatsApp, uno por póliza). Sin wildcard, la lista tiene
                // 0 o 1 elemento y el comportamiento es idéntico al histórico. Tope de seguridad
                // para no inundar al cliente.
                const int MaxWildcardMatches = 10;
                List<JsonElement> matches;
                if (field.FieldPath?.Contains("[*]") == true)
                {
                    matches = ResolveWildcardPaths(root, field.FieldPath);
                    if (matches.Count > MaxWildcardMatches)
                    {
                        logger.LogWarning("[OutputInterpreter] Wildcard '{Field}' devolvió {N} coincidencias — se procesan {Max}.",
                            field.FieldPath, matches.Count, MaxWildcardMatches);
                        matches = matches.Take(MaxWildcardMatches).ToList();
                    }
                }
                else
                {
                    var single = ResolveDotPath(root, field.FieldPath ?? "");
                    matches = single is { } s ? [s] : [];
                }

                if (matches.Count == 0)
                {
                    if (field.Required)
                        logger.LogWarning("[OutputInterpreter] Campo requerido '{Field}' ausente en respuesta de {Action}",
                            field.FieldPath, context.ActionName);
                    continue;
                }

                foreach (var rawMatch in matches)
                {
                    JsonElement? raw = rawMatch;
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
                                await HandleWhatsAppMediaAsync(raw, field, context, agentParts, mediaUrls, ct);
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
            }

            return ActionResult.Ok(
                dataForAgent: agentParts.Count > 0 ? string.Join(" | ", agentParts) : null,
                shouldEscalate: shouldEscalate) with
            {
                MediaUrls = mediaUrls.Count > 0 ? mediaUrls : null
            };
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Resuelve un path dot-notation en el JSON. Soporta índices de array con sufijo `[n]`:
    /// "documento.archivo" → json.documento.archivo; "documentos[0].base64" → primer elemento
    /// del array `documentos`, campo `base64`. Devuelve null si el path no existe o el índice
    /// está fuera de rango. Retrocompatible: paths sin `[n]` se comportan igual que antes.
    /// </summary>
    private static JsonElement? ResolveDotPath(JsonElement root, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var current = root;

        foreach (var rawPart in path.Split('.'))
        {
            var part = rawPart;
            int? index = null;

            // Soporte de índice de array: "documentos[0]" → part="documentos", index=0.
            var br = part.IndexOf('[');
            if (br >= 0 && part.EndsWith("]"))
            {
                var idxStr = part.Substring(br + 1, part.Length - br - 2);
                if (int.TryParse(idxStr, out var i)) index = i;
                part = part.Substring(0, br);
            }

            // Paso 1: navegar por la propiedad (si el segmento tiene nombre).
            if (!string.IsNullOrEmpty(part))
            {
                if (current.ValueKind != JsonValueKind.Object) return null;
                if (!current.TryGetProperty(part, out var next)) return null;
                current = next;
            }

            // Paso 2: indexar el array (si el segmento traía `[n]`).
            if (index is { } idx)
            {
                if (current.ValueKind != JsonValueKind.Array) return null;
                if (idx < 0 || idx >= current.GetArrayLength()) return null;
                current = current[idx];
            }
        }

        return current;
    }

    /// <summary>
    /// Resuelve un path con wildcards `[*]` devolviendo TODAS las coincidencias.
    /// Ej: "polizas[*].documentos[*].base64" → cada base64 de cada documento de cada póliza.
    /// Soporta mezclar índices fijos y wildcards ("polizas[*].documentos[0].base64").
    /// </summary>
    private static List<JsonElement> ResolveWildcardPaths(JsonElement root, string path)
    {
        var results = new List<JsonElement>();
        Walk(root, path.Split('.'), 0, results);
        return results;

        static void Walk(JsonElement current, string[] parts, int partIdx, List<JsonElement> results)
        {
            if (partIdx >= parts.Length)
            {
                results.Add(current);
                return;
            }

            var part = parts[partIdx];
            string? indexSpec = null;

            var br = part.IndexOf('[');
            if (br >= 0 && part.EndsWith("]"))
            {
                indexSpec = part.Substring(br + 1, part.Length - br - 2); // "*" o "n"
                part = part.Substring(0, br);
            }

            if (!string.IsNullOrEmpty(part))
            {
                if (current.ValueKind != JsonValueKind.Object) return;
                if (!current.TryGetProperty(part, out var next)) return;
                current = next;
            }

            if (indexSpec is null)
            {
                Walk(current, parts, partIdx + 1, results);
                return;
            }

            if (current.ValueKind != JsonValueKind.Array) return;

            if (indexSpec == "*")
            {
                foreach (var item in current.EnumerateArray())
                    Walk(item, parts, partIdx + 1, results);
            }
            else if (int.TryParse(indexSpec, out var idx) && idx >= 0 && idx < current.GetArrayLength())
            {
                Walk(current[idx], parts, partIdx + 1, results);
            }
        }
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
    /// Recibe un base64 del webhook, lo sube a Azure Blob Storage para obtener una URL
    /// pública, y lo envía como documento por WhatsApp usando el channel provider del tenant.
    /// </summary>
    private async Task HandleWhatsAppMediaAsync(
        JsonElement? raw,
        OutputField field,
        OutputContext context,
        List<string> agentParts,
        List<string> mediaUrls,
        CancellationToken ct)
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

            // 1. Subir a Azure Blob Storage para obtener URL pública
            var ext = field.MimeType switch
            {
                "application/pdf" => "pdf",
                "image/png" => "png",
                "image/jpeg" => "jpg",
                _ => "bin"
            };
            var filename = $"webhook-media/{context.TenantId}/{DateTime.UtcNow:yyyyMMdd}/{Guid.NewGuid()}.{ext}";
            var publicUrl = await blobStorage.UploadWhatsAppMediaAsync(filename, bytes, field.MimeType, ct);

            // RESPALDO + visibilidad en el Monitor: registramos la URL del blob apenas se sube,
            // independiente de si el envío por WhatsApp después falla. El handler la adjunta al
            // mensaje del agente como [media:URL].
            mediaUrls.Add(publicUrl);

            logger.LogInformation("[OutputInterpreter] Media subida a blob: {Url}", publicUrl);

            // 2. Enviar como documento por WhatsApp
            var provider = await channelFactory.GetProviderAsync(context.TenantId, ct);
            if (provider is not null && !string.IsNullOrEmpty(context.ContactPhone))
            {
                var sendResult = await provider.SendMessageAsync(new SendMessageRequest(
                    To: context.ContactPhone,
                    Body: field.Label ?? "Documento",
                    MediaUrl: publicUrl,
                    MediaType: "document",
                    Filename: $"{field.Label ?? "documento"}.{ext}"
                ), ct);

                if (sendResult.Success)
                {
                    logger.LogInformation("[OutputInterpreter] Documento enviado por WhatsApp a {Phone}", context.ContactPhone);
                    agentParts.Add($"El {field.Label} fue enviado exitosamente al cliente por WhatsApp.");
                }
                else
                {
                    logger.LogWarning("[OutputInterpreter] Error enviando documento por WhatsApp: {Error}", sendResult.Error);
                    agentParts.Add($"No se pudo enviar el {field.Label} por WhatsApp. El cliente puede descargarlo desde: {publicUrl}");
                }
            }
            else
            {
                // Fallback: si no hay provider, dar la URL directa
                agentParts.Add($"Documento disponible: {publicUrl}");
            }
        }
        catch (FormatException)
        {
            logger.LogWarning("[OutputInterpreter] Base64 corrupto en {Field} para {Action}",
                field.FieldPath, context.ActionName);
            agentParts.Add($"Error generando {field.Label}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[OutputInterpreter] Error procesando media para {Action}", context.ActionName);
            agentParts.Add($"Error procesando {field.Label}");
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
