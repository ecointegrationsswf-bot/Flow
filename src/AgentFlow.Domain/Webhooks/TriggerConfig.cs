namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Action Trigger Protocol — Capa 1: metadata que define cuándo y cómo el agente
/// debe disparar una acción. Se almacena embebida dentro del JSON ActionConfigs
/// del CampaignTemplate (ningún cambio de schema de BD).
///
/// El agente lee estos campos en su system prompt a través del bloque
/// ACCIONES DISPONIBLES que construye IActionPromptBuilder. Los usa para decidir
/// cuándo emitir [ACTION:slug] y qué [PARAM:nombre=valor] incluir.
///
/// Todos los campos son opcionales a nivel de serialización para mantener
/// retrocompatibilidad: una campaña sin TriggerConfig en ninguna acción sigue
/// funcionando como antes y el builder simplemente no produce bloque.
/// </summary>
public class TriggerConfig
{
    /// <summary>
    /// Descripción en lenguaje natural de cuándo usar esta acción.
    /// El agente la lee directamente en su system prompt.
    /// Ejemplo: "Usar cuando el cliente confirma explícitamente que quiere realizar un pago."
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Frases reales del usuario que activan esta acción. Entre 3 y 7 para que
    /// Claude tenga suficiente referencia semántica sin inflar el prompt.
    /// NO son keywords exactas — el agente las usa como ejemplos, no como match literal.
    /// </summary>
    public List<string>? TriggerExamples { get; init; }

    /// <summary>
    /// Nombres de los parámetros (RequiredParams del InputSchema) que el agente
    /// DEBE haber confirmado con el usuario antes de disparar la acción.
    /// Si está vacío o null: la acción se puede ejecutar de inmediato al detectar la intención.
    /// </summary>
    public List<string>? RequiresConfirmation { get; init; }

    /// <summary>
    /// Pregunta sugerida que el agente puede usar cuando detecta la intención pero
    /// le faltan parámetros por confirmar. El agente puede reformularla según el
    /// contexto de la conversación — no tiene que usarla literal.
    /// Ejemplo: "¿Cuál es el monto que deseas pagar?"
    /// </summary>
    public string? ClarificationPrompt { get; init; }

    /// <summary>
    /// Heurística rápida: ¿esta config tiene información útil para inyectar en el prompt?
    /// Una TriggerConfig sin description es efectivamente no configurada.
    /// </summary>
    public bool HasMeaningfulContent() => !string.IsNullOrWhiteSpace(Description);
}
