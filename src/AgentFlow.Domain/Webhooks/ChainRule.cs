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

    /// <summary>
    /// Si true, después de que el chain termine exitosamente el orquestador
    /// hace una SEGUNDA invocación al LLM con el LastActionResult inyectado y
    /// una directiva "POST_CHAIN: redactá la respuesta final al cliente sin
    /// emitir más [ACTION:...]". Reemplaza el replyText preliminar del primer
    /// turno ("validando...") por uno natural que responde a la pregunta
    /// original con los datos en mano.
    ///
    /// Costo: 1 round-trip extra al LLM (~2s latencia, tokens adicionales).
    /// Usar para acciones cuya respuesta debe presentarse al cliente
    /// inmediatamente (validación de identidad exitosa → mostrar pólizas;
    /// consulta de saldo; etc.). Funciona también con `Then = null`.
    /// </summary>
    public bool RegenerateReply { get; init; }
}

/// <summary>
/// Condición sobre el contexto de evaluación del chain. Puede ser:
///  - HOJA: define <see cref="Path"/> + <see cref="Operator"/> (+ <see cref="Value"/>).
///  - COMPUESTA: define <see cref="AllOf"/> (AND) o <see cref="AnyOf"/> (OR) — listas
///    anidables de sub-condiciones. La negación se logra con operadores `notEquals`/`isNull`.
///
/// <para><b>Contexto de evaluación (Fase 1 del motor de flujos):</b></para>
/// El path se resuelve sobre un objeto JSON unificado cuyo RAÍZ es el response del
/// webhook (compat: `status`, `data.code`) más namespaces inyectados:
///  - `llm.intent`, `llm.confidence` — resultado del LLM del turno (clasificación/confianza).
///  - (futuro) `auth.isAuthenticated`, `session.*`, `datos_consultados.*`.
/// Así el encadenamiento puede depender de la RESPUESTA del webhook **o** del RESULTADO del LLM.
///
/// <para><b>Operadores soportados:</b></para>
/// `equals`, `notEquals`, `contains`, `startsWith`, `isNotNull`, `isNull`,
/// `gt`, `gte`, `lt`, `lte` (numéricos). Operador desconocido → no matchea (fail-safe).
/// Comparaciones de string case-insensitive; numéricas vía decimal.
/// </summary>
public class ChainCondition
{
    /// <summary>Path al campo (ej: "status", "data.code", "llm.intent"). Vacío si es condición compuesta.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Operador. Ver lista en el doc de la clase. Default "equals".</summary>
    public string Operator { get; init; } = "equals";

    /// <summary>Valor a comparar contra el contenido del path. String (CI) o numérico según el operador.</summary>
    public string? Value { get; init; }

    /// <summary>AND: matchea solo si TODAS las sub-condiciones matchean. Anidable. Tiene prioridad sobre Path.</summary>
    public List<ChainCondition>? AllOf { get; init; }

    /// <summary>OR: matchea si AL MENOS UNA sub-condición matchea. Anidable. Tiene prioridad sobre Path.</summary>
    public List<ChainCondition>? AnyOf { get; init; }
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
