namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Resuelve si después de ejecutar una acción A hay que disparar
/// automáticamente otra acción B (auto-encadenamiento server-side).
///
/// Lee las ChainRules embebidas en el contract de la acción A (vía
/// ActionDefinition.DefaultWebhookContract del clon tenant-specific) y
/// las evalúa contra el JSON crudo del response. Devuelve el slug de la
/// siguiente acción + el mensaje opcional a apendar al replyText, o null
/// si no hay match.
///
/// La razón de existir como servicio independiente: el orquestador
/// (ProcessIncomingMessageCommand) necesita saber qué encadenar sin
/// duplicar la lógica de lectura del bundle ni de evaluación de JSONPath.
/// </summary>
public interface IActionChainResolver
{
    /// <summary>
    /// Dado el slug de la acción recién ejecutada, el tenant y el JSON crudo
    /// de su respuesta, devuelve la decisión de encadenamiento (o null si no
    /// hay regla aplicable / el JSON no matchea).
    ///
    /// <paramref name="extraContextJson"/> (opcional, Fase 1 del motor de flujos):
    /// objeto JSON con namespaces extra que se MERGEAN al raíz del contexto de
    /// evaluación junto al response del webhook — ej: `{"llm":{"intent":"reclamos","confidence":0.92}}`.
    /// Permite que las ChainRules condicionen por el RESULTADO del LLM, no solo por
    /// el JSON del webhook. Si es null, el comportamiento es idéntico al anterior.
    /// </summary>
    Task<ChainDecision?> GetNextActionAsync(
        string executedSlug,
        Guid tenantId,
        string? rawResponseJson,
        CancellationToken ct,
        string? extraContextJson = null);

    /// <summary>
    /// Motor de flujos — Fase 2. Devuelve la configuración de autenticación de una acción
    /// (¿requiere auth?, política de qué resultado autentica, mensaje si se bloquea), leída
    /// del contrato per-tenant → global. Si no hay contrato, devuelve "no requiere auth".
    /// La usa el orquestador para el GATE determinístico (bloquear acciones confidenciales
    /// sin auth) y para SETEAR el estado de auth tras una validación exitosa.
    /// </summary>
    Task<ActionAuthConfig> GetAuthConfigAsync(Guid tenantId, string slug, CancellationToken ct);

    /// <summary>
    /// Escalamiento robusto — Fase D. Devuelve el tope de ejecuciones por conversación de una
    /// acción (leído del contrato per-tenant → global). Lo usa el orquestador para un GATE
    /// determinístico anti-loop: p.ej. limitar cuántas veces se puede re-disparar INSURED_INITIATE
    /// (reenvío de código 2FA) antes de escalar a humano. Sin cap configurado → <c>MaxCalls=null</c>
    /// → comportamiento idéntico al actual (sin límite).
    /// </summary>
    Task<ActionCallCap> GetCallCapAsync(Guid tenantId, string slug, CancellationToken ct);
}

/// <summary>
/// Escalamiento robusto — Fase D. Tope de ejecuciones por conversación de una acción.
/// <c>MaxCalls=null</c> = sin límite (default). Cuando se alcanza, el orquestador NO ejecuta la
/// acción: responde <c>ExhaustedMessage</c> al cliente y marca la conversación para escalar.
/// </summary>
public record ActionCallCap(int? MaxCalls, string? ExhaustedMessage);

/// <summary>
/// Config de auth de una acción (Fase 2). `RequiresAuth`=true exige auth previa para
/// ejecutar. `AuthPolicy` (si no es null) declara qué resultado de ESTA acción autentica.
/// `AuthRequiredMessage` es el texto a responder cuando se bloquea por falta de auth.
/// </summary>
public record ActionAuthConfig(bool RequiresAuth, AuthPolicy? AuthPolicy, string? AuthRequiredMessage);

/// <summary>
/// Resultado de la evaluación de ChainRules. Contiene el slug de la siguiente
/// acción y opcionalmente un texto a apendar al replyText del agente cuando
/// el chain ejecute exitosamente (placeholders ya pueden estar interpolados o
/// no — depende de quién haga la interpolación; en el MVP el handler lo hace).
///
/// `NextSlug` puede ser null cuando la regla matcheó con `then=null` y solo
/// quería marcar la rama (por ejemplo para activar RegenerateReply sin cadenar).
/// </summary>
public record ChainDecision(string? NextSlug, string? SuccessMessageTemplate, bool RegenerateReply);
