using System.Text.Json.Nodes;

namespace AgentFlow.Domain.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 4 (Salida, Dirección B). Constantes y contratos canónicos del
/// API de Integración de Ludo (doc "LudoCRM_API_Integracion" v1.0, jun-2026):
/// <list type="bullet">
///   <item><c>POST {base}/api/integration/oportunidad</c> — upsert por teléfono.</item>
///   <item><c>PUT {base}/api/integration/oportunidad/{oportunidadId}/fase</c> — mover fase (idempotente).</item>
///   <item><c>POST {base}/api/integration/notas</c> — nota de seguimiento.</item>
/// </list>
/// Auth: token por tenant en header <c>X-Api-Key</c> (formato jam_...). Envoltorio de respuesta:
/// { success, data, message, errors }.
///
/// <para>El placeholder <c>{oportunidadId}</c> de mover_fase lo resuelve el
/// <b>LudoActionEnricher</b> en runtime (GET /prospecto por teléfono). El parámetro
/// <c>etapa</c> que emite el LLM se traduce a <c>faseId</c> vía StageLabelMap.</para>
/// </summary>
public static class LudoIntegrationDefaults
{
    public const string RegistrarOportunidadSlug = "registrar_oportunidad";
    public const string MoverFaseSlug = "mover_fase";
    public const string RegistrarNotaSlug = "registrar_nota";

    /// <summary>Placeholder de la URL de mover_fase que el enricher reemplaza en runtime.</summary>
    public const string OpportunityIdPlaceholder = "{oportunidadId}";

    /// <summary>Base URL productiva del API de Ludo (site14). Overridable por payload de provisioning.</summary>
    public const string DefaultApiBaseUrl = "https://jamconsulting-004-site14.site4future.com";

    public static readonly string[] AllSlugs =
        [RegistrarOportunidadSlug, MoverFaseSlug, RegistrarNotaSlug];

    public static bool IsLudoActionSlug(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && AllSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Detecta el consentimiento FORMAL del protocolo de venta ("ACEPTO"): mensaje corto
    /// cuyo contenido es la palabra acepto (tolerante a "sí, acepto", tildes y signos),
    /// sin negación. Un "acepto" dentro de una frase larga NO cuenta — el protocolo exige
    /// la palabra como respuesta al resumen final.
    /// </summary>
    public static bool IsFormalConsent(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in message.Normalize(System.Text.NormalizationForm.FormD))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue; // quita tildes
            if (char.IsLetter(ch)) current.Append(char.ToLowerInvariant(ch));
            else if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) tokens.Add(current.ToString());

        return tokens.Count is > 0 and <= 4
            && tokens.Contains("acepto")
            && !tokens.Contains("no");
    }

    /// <summary>
    /// Construye el ContractJson (shape ActionConfigBundleJson) de una acción Ludo.
    /// <paramref name="apiKey"/> vacío ⇒ contrato plantilla (DefaultWebhookContract global);
    /// con valor ⇒ contrato per-tenant (TenantActionContract).
    /// </summary>
    public static string BuildContractJson(string slug, string? apiBaseUrl, string? apiKey)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? DefaultApiBaseUrl
            : apiBaseUrl.TrimEnd('/');

        return slug.ToLowerInvariant() switch
        {
            RegistrarOportunidadSlug => Contract(
                url: $"{baseUrl}/api/integration/oportunidad",
                method: "POST",
                apiKey: apiKey,
                fields:
                [
                    SystemField("telefono", "contact.phone"),
                    // nombre viene de la CONVERSACIÓN (confirmado por el cliente), no del
                    // perfil de WhatsApp (alias poco confiables tipo "VL"). requiresConfirmation
                    // bloquea la ejecución hasta que el agente lo capture.
                    ConversationField("nombre", "nombre"),
                    ConversationField("objetivo", "objetivo"),
                    ConversationField("monto", "monto", dataType: "number"),
                    ConversationField("resumenConversacion", "resumen"),
                ],
                trigger: Trigger(
                    description: "Registra una oportunidad de venta en el CRM cuando el cliente muestra una intención calificada (quiere cotizar, comprar o contratar). ANTES de ejecutarla, confirmá el NOMBRE COMPLETO del cliente en la conversación (si no lo dio claramente, pedíselo) y pasalo en [PARAM:nombre=...] — nunca uses el alias del perfil de WhatsApp.",
                    examples:
                    [
                        "quiero cotizar una póliza para mi auto",
                        "me interesa contratar el seguro",
                        "cuánto me costaría asegurar mi casa",
                    ],
                    requiresConfirmation: ["nombre"],
                    clarificationPrompt: "Para registrar su solicitud, ¿me confirma su nombre completo, por favor?")),

            MoverFaseSlug => Contract(
                url: $"{baseUrl}/api/integration/oportunidad/{OpportunityIdPlaceholder}/fase",
                method: "PUT",
                apiKey: apiKey,
                fields:
                [
                    // El enricher setea EXACTAMENTE UNO: faseId (mapeado por StageLabelMap)
                    // o faseNombre (fallback). Ludo prioriza faseNombre si van ambos.
                    ConversationField("faseId", "faseId", dataType: "number"),
                    ConversationField("faseNombre", "faseNombre"),
                    ConversationField("motivo", "motivo"),
                ],
                trigger: Trigger(
                    description: "Avanza la oportunidad del cliente a la fase del pipeline cuyo criterio se cumple. OBLIGATORIO: incluí SIEMPRE [PARAM:etapa=<nombre EXACTO de la etapa destino>] (ej. [PARAM:etapa=INTERESADO]) — sin ese parámetro la acción no hace nada.",
                    examples:
                    [
                        "confirmo, quiero avanzar con la póliza",
                        "ya envié los documentos que me pidieron",
                    ])),

            RegistrarNotaSlug => Contract(
                url: $"{baseUrl}/api/integration/notas",
                method: "POST",
                apiKey: apiKey,
                fields:
                [
                    SystemField("telefono", "contact.phone"),
                    ConversationField("contenido", "contenido"),
                    StaticField("tipoNota", "WhatsApp"),
                ],
                trigger: Trigger(
                    description: "Registra una nota de seguimiento en el CRM con información relevante que dio el cliente (acuerdos, dudas importantes, compromisos).",
                    examples:
                    [
                        "el cliente pidió que lo llamen la próxima semana",
                        "quedó pendiente enviar la cotización por correo",
                    ])),

            _ => throw new ArgumentOutOfRangeException(nameof(slug), slug, "Slug Ludo desconocido."),
        };
    }

    // ── Builders del JSON (shape ActionConfigBundleJson, keys camelCase) ─────────────

    private static string Contract(string url, string method, string? apiKey,
        JsonObject[] fields, JsonObject trigger)
    {
        var o = new JsonObject
        {
            ["webhookUrl"] = url,
            ["webhookMethod"] = method,
            ["contentType"] = "application/json",
            ["structure"] = "flat",
            ["authType"] = "ApiKey",
            ["authValue"] = apiKey ?? string.Empty,
            ["apiKeyHeaderName"] = "X-Api-Key",
            ["timeoutSeconds"] = 15,
            ["inputSchema"] = new JsonObject
            {
                ["contentType"] = "application/json",
                ["structure"] = "flat",
                ["fields"] = new JsonArray(fields.Select(f => (JsonNode)f).ToArray()),
            },
            ["triggerConfig"] = trigger,
        };
        return o.ToJsonString();
    }

    private static JsonObject SystemField(string fieldPath, string sourceKey) => new()
    {
        ["fieldPath"] = fieldPath,
        ["sourceType"] = "system",
        ["sourceKey"] = sourceKey,
        ["dataType"] = "string",
    };

    private static JsonObject ConversationField(string fieldPath, string sourceKey, string dataType = "string") => new()
    {
        ["fieldPath"] = fieldPath,
        ["sourceType"] = "conversation",
        ["sourceKey"] = sourceKey,
        ["dataType"] = dataType,
    };

    private static JsonObject StaticField(string fieldPath, string value) => new()
    {
        ["fieldPath"] = fieldPath,
        ["sourceType"] = "static",
        ["staticValue"] = value,
        ["dataType"] = "string",
    };

    private static JsonObject Trigger(string description, string[] examples,
        string[]? requiresConfirmation = null, string? clarificationPrompt = null)
    {
        var o = new JsonObject
        {
            ["description"] = description,
            ["triggerExamples"] = new JsonArray(examples.Select(e => (JsonNode)e).ToArray()),
            ["requiresConfirmation"] = new JsonArray((requiresConfirmation ?? []).Select(r => (JsonNode)r).ToArray()),
        };
        if (!string.IsNullOrWhiteSpace(clarificationPrompt))
            o["clarificationPrompt"] = clarificationPrompt;
        return o;
    }
}
