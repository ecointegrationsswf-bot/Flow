namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Action Trigger Protocol — Capa 2: construcción del bloque "ACCIONES DISPONIBLES"
/// que se inyecta al final del system prompt del agente.
///
/// Dado un campaign template y un tenant, lee los TriggerConfig embebidos en
/// ActionConfigs y construye un bloque de texto en formato Markdown que el
/// agente lee en cada turno. Este bloque contiene:
///   - Descripción de cuándo usar cada acción
///   - Ejemplos de frases del usuario que la activan
///   - Campos que el agente debe confirmar antes de ejecutar
///   - Formato exacto del tag [ACTION:slug] y sus [PARAM:nombre=valor]
///
/// Si la campaña no tiene TriggerConfig configurado en ninguna acción,
/// devuelve string vacío y el agente opera como antes (retrocompatible).
///
/// FASE 0: implementación vacía (siempre devuelve ""). El plumbing de DI y
/// llamada desde el handler queda listo para que la Fase 2 solo implemente
/// la lógica real sin tocar el flujo.
/// </summary>
public interface IActionPromptBuilder
{
    /// <summary>
    /// Construye el bloque ACCIONES DISPONIBLES para el system prompt del agente.
    /// Equivalente a GetCatalogAsync(...).Block — azúcar para callers que solo
    /// necesitan el texto.
    /// </summary>
    /// <param name="campaignTemplateId">Maestro de campaña activo en la conversación. Null si no hay maestro (p.ej. conversación ad-hoc).</param>
    /// <param name="tenantId">Tenant actual — toda query debe filtrar por esto.</param>
    /// <returns>Bloque de texto Markdown, o string.Empty si no hay acciones con TriggerConfig.</returns>
    Task<string> BuildAsync(
        Guid? campaignTemplateId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Devuelve el catálogo completo (texto renderizado + diccionario slug→TriggerConfig)
    /// para poder, en el mismo turno, inyectar el bloque al system prompt y además
    /// validar el [ACTION:slug] emitido por el agente contra el whitelist.
    ///
    /// El catálogo se cachea internamente por (tenantId, campaignTemplateId) con TTL 5 min.
    /// </summary>
    Task<ActionCatalog> GetCatalogAsync(
        Guid? campaignTemplateId,
        Guid tenantId,
        CancellationToken ct = default);
}
