using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Genera el mensaje inicial de campaña usando el LLM del tenant. Reemplaza
/// el template hardcoded del CampaignDispatcherService por la misma lógica
/// que usa N8nCallbackController (Claude + prompt del CampaignTemplate +
/// contexto desde ContactDataJson).
/// </summary>
public interface IInitialMessageGenerator
{
    /// <summary>
    /// Devuelve el resultado de la generación con código de error específico.
    /// El caller decide cómo mostrar el error al admin (cada código tiene una
    /// causa raíz distinta y una acción correctiva distinta).
    /// </summary>
    Task<MessageGenerationResult> GenerateAsync(
        Campaign campaign,
        CampaignContact contact,
        CancellationToken ct = default);
}

/// <summary>
/// Resultado de la generación con código de error accionable. Si <see cref="Text"/>
/// no es null, fue exitoso. Si es null, <see cref="ErrorCode"/> indica la causa raíz.
/// </summary>
public record MessageGenerationResult(string? Text, MessageGenerationError ErrorCode, string? Detail = null)
{
    public static MessageGenerationResult Ok(string text) => new(text, MessageGenerationError.None);
    public static MessageGenerationResult Fail(MessageGenerationError code, string? detail = null) => new(null, code, detail);
}

/// <summary>
/// Códigos de error específicos. Cada uno se mapea a un mensaje accionable
/// distinto que se le muestra al admin del tenant (en DispatchError del contacto).
/// </summary>
public enum MessageGenerationError
{
    /// <summary>Éxito — el texto fue generado correctamente.</summary>
    None = 0,

    /// <summary>
    /// El tenant no tiene <c>LlmApiKey</c> configurada. Solución: ir a
    /// Configuración → Tenant → pegar la API key de Anthropic.
    /// </summary>
    NoApiKey = 1,

    /// <summary>
    /// El maestro de campaña no tiene <c>SystemPrompt</c> local ni
    /// PromptTemplateIds asociados. Solución: editar el maestro → tab Prompt.
    /// </summary>
    NoPrompt = 2,

    /// <summary>
    /// El PromptTemplate global referenciado por el maestro está vacío.
    /// Solución: editar el PromptTemplate → guardar el contenido.
    /// </summary>
    EmptyPromptTemplate = 3,

    /// <summary>
    /// El contacto no tiene <c>ContactDataJson</c> poblado. Causa probable:
    /// bug en el flujo de carga del archivo (FixedFormatCampaignService /
    /// DelinquencyProcessor). Re-cargar el archivo o reportar al equipo de TI.
    /// </summary>
    NoContactData = 4,

    /// <summary>
    /// El LLM tiró excepción al ser invocado (timeout, API key inválida,
    /// modelo no disponible, etc.). Detail trae el mensaje original.
    /// </summary>
    LlmException = 5,

    /// <summary>
    /// El LLM respondió pero el texto vino vacío. Suele indicar prompt mal
    /// formado o contexto insuficiente.
    /// </summary>
    EmptyLlmResponse = 6,

    /// <summary>
    /// El LLM emitió "output meta" — texto técnico no apto para el cliente
    /// (refusal, instrucciones del prompt, JSON crudo). Detectado por
    /// guardrail del dispatcher.
    /// </summary>
    MetaOutput = 7,
}
