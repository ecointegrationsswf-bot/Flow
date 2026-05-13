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
/// </summary>
public record LastActionResult(
    string Slug,
    string? DataForAgent,
    DateTime ExecutedAt)
{
    /// <summary>Ventana de tiempo durante la cual el resultado se considera relevante.</summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(10);

    /// <summary>¿El resultado aún es fresco? Evaluado contra UTC ahora.</summary>
    public bool IsFresh() => DateTime.UtcNow - ExecutedAt < FreshnessWindow;
}
