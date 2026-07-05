namespace AgentFlow.Domain.Provisioning;

/// <summary>
/// Integración Ludo CRM — Fase 3. Petición para generar (con LLM) un maestro de campaña.
/// El generador produce un BORRADOR editable; el Admin lo refina y activa.
/// </summary>
public sealed record GenerateTemplateRequest(
    string TipoNegocio,                 // seguro | restaurante | inmobiliario | ...
    string AgentSlug,
    string Objetivo,                    // objetivo en lenguaje natural
    IReadOnlyList<StageInfo> Etapas);

/// <summary>Etapa vigente del pipeline (para que el LLM defina criterios de avance).</summary>
public sealed record StageInfo(string Nombre, int Orden);

/// <summary>Criterio de avance a una etapa, generado por el LLM.</summary>
public sealed record StageCriterion(string Etapa, string Criterio);

/// <summary>
/// Resultado de la generación. <see cref="UsedLlm"/> indica si el contenido vino del LLM
/// (true) o de un fallback determinista (false, ej: LLM caído o JSON inválido). En la vertical
/// "seguro" el <see cref="SystemPrompt"/> SIEMPRE incluye el gate de autenticación vetado,
/// independientemente de lo que devuelva el LLM.
/// </summary>
public sealed record GeneratedTemplate(
    string SystemPrompt,
    IReadOnlyList<string> SuggestedActionSlugs,
    IReadOnlyList<StageCriterion> StageCriteria,
    bool UsedLlm);

/// <summary>
/// Genera un maestro de campaña asistido por LLM. NUNCA lanza: ante cualquier fallo del LLM
/// devuelve un <see cref="GeneratedTemplate"/> de fallback determinista (UsedLlm=false), para
/// que el aprovisionamiento (que es transaccional) no se rompa por una caída del modelo.
/// </summary>
public interface ICampaignTemplateGenerator
{
    /// <param name="tenantApiKey">
    /// API key del tenant si existe; si es null/vacía, el generador usa la key global de config.
    /// En provisioning el tenant es net-new y no tiene key todavía → se usa la global.
    /// </param>
    Task<GeneratedTemplate> GenerateAsync(GenerateTemplateRequest req, string? tenantApiKey, CancellationToken ct);
}
