namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Action Trigger Protocol — Capa 3 (Fase 4).
/// Resultado de la última acción ejecutada en esta conversación, persistido en
/// SessionState (Redis) para que esté disponible en el siguiente turno del agente.
///
/// Resuelve el Flujo D del documento: cuando un webhook devuelve datos útiles
/// (ej: un link de pago generado), el agente los recibe inyectados en su system
/// prompt del siguiente turno bajo la sección "RESULTADO DE ACCIÓN PREVIA" y
/// los puede incluir en su respuesta al cliente.
///
/// Se considera "fresco" mientras la antigüedad sea menor a
/// <see cref="FreshnessWindow"/>; más viejo que eso, el runner lo ignora.
///
/// <para><b>Comportamiento con auto-encadenamiento (ChainRules):</b></para>
/// Cuando una acción A dispara automáticamente otra acción B vía ChainRule,
/// el `LastActionResult` que se persiste es el de A (el eslabón ORIGEN, no el
/// último). Razón: B suele ser una acción "puente" sin response útil
/// (ej: SEND_2FA_CODE_EMAIL); los datos que el LLM/PayloadBuilder necesitarán
/// en el siguiente turno (ej: `idCodigo` para INSURED_VALIDATE) viven en el
/// response de A. Desde la perspectiva del LLM, el turno fue "invocó A" —
/// los chains son transparentes.
/// </summary>
public record LastActionResult(
    string Slug,
    string? DataForAgent,
    DateTime ExecutedAt,
    /// <summary>
    /// JSON crudo del response del eslabón ORIGEN del chain. Lo usa el handler
    /// del siguiente turno para aplanar campos como "lastActionResult.idCodigo"
    /// al SystemContext, habilitando InputSchema fields con sourceType=lastActionResult.
    /// Null para acciones legacy sin chain o si el response no era JSON.
    /// </summary>
    string? RawResponseJson = null)
{
    /// <summary>Ventana de tiempo durante la cual el resultado se considera relevante.</summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(10);

    /// <summary>¿El resultado aún es fresco? Evaluado contra UTC ahora.</summary>
    public bool IsFresh() => DateTime.UtcNow - ExecutedAt < FreshnessWindow;
}
