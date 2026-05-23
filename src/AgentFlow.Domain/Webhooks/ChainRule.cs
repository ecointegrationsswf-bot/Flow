namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Regla de auto-encadenamiento de acciones. Se evalúa server-side contra
/// el JSON crudo de la respuesta de una acción A. Si matchea, dispara
/// inmediatamente la acción B sin pasar por el LLM.
///
/// Objetivo: eliminar la decisión del agente IA en transiciones
/// determinísticas (ej: INSURED_INITIATE con status=CODIGO_GENERADO debe
/// SIEMPRE encadenar SEND_2FA_CODE_EMAIL). El LLM solo redacta una vez,
/// al final del chain, con todo el contexto resuelto.
///
/// Se almacena dentro del JSON `DefaultWebhookContract` de cada
/// ActionDefinition, por tenant. Cero migraciones de schema.
///
/// Guards aplicados por el orquestador (ProcessIncomingMessageCommand):
///  - Max depth = 3 encadenamientos por turno
///  - Detección de ciclos: misma slug 2 veces en el turno corta el chain
///  - Solo la PRIMERA regla que matchea se dispara (orden importa)
///  - Si la acción encadenada falla, es best-effort: queda en
///    WebhookDispatchLogs pero no rompe el flujo de la acción origen.
/// </summary>
public class ChainRule
{
    /// <summary>Condición a evaluar sobre el JSON response.</summary>
    public ChainCondition When { get; init; } = new();

    /// <summary>
    /// Qué encadenar. Null/Empty = "no encadenar nada" (rama documentada
    /// explícitamente para que el admin marque que esa respuesta termina ahí).
    /// </summary>
    public ChainTarget? Then { get; init; }
}

/// <summary>
/// Condición sobre el JSON response. MVP solo soporta operator=equals.
/// Path es JsonPath simple: "status", "data.codigo", "user.email", etc.
/// Path se resuelve con JsonElement.TryGetProperty recursivo.
/// </summary>
public class ChainCondition
{
    /// <summary>Path al campo en el JSON response (ej: "status", "data.code").</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>MVP: solo "equals". Futuro: "notEquals", "contains", "isNotNull".</summary>
    public string Operator { get; init; } = "equals";

    /// <summary>Valor a comparar contra el contenido del path. Comparación case-insensitive a string.</summary>
    public string? Value { get; init; }
}

/// <summary>Acción a disparar cuando la condición matchea.</summary>
public class ChainTarget
{
    /// <summary>Slug de la acción a encadenar (debe estar asignada al mismo tenant).</summary>
    public string ActionSlug { get; init; } = string.Empty;

    /// <summary>
    /// Mensaje opcional que se apendica al replyText del agente cuando el chain
    /// ejecuta exitosamente. Soporta interpolación de placeholders `{path}` que
    /// se resuelven contra el JSON response del eslabón ORIGEN (la acción que
    /// disparó la regla, NO la encadenada).
    ///
    /// Ejemplo para 2FA:
    ///   "Te envié un código de 6 dígitos al correo {correoEnmascarado}.
    ///    Por favor ingrésalo aquí en este chat."
    ///
    /// El path es case-insensitive, dot-notation simple (`status`, `data.code`).
    /// Placeholders no encontrados se sustituyen por string.Empty.
    /// </summary>
    public string? SuccessMessage { get; init; }
}
