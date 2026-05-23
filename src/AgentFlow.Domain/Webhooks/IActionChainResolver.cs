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
    /// </summary>
    Task<ChainDecision?> GetNextActionAsync(
        string executedSlug,
        Guid tenantId,
        string? rawResponseJson,
        CancellationToken ct);
}

/// <summary>
/// Resultado de la evaluación de ChainRules. Contiene el slug de la siguiente
/// acción y opcionalmente un texto a apendar al replyText del agente cuando
/// el chain ejecute exitosamente (placeholders ya pueden estar interpolados o
/// no — depende de quién haga la interpolación; en el MVP el handler lo hace).
/// </summary>
public record ChainDecision(string NextSlug, string? SuccessMessageTemplate);
