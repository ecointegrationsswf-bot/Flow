namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Procesa la respuesta de un webhook de morosidad: extrae ítems, normaliza teléfonos,
/// agrupa por contacto y — según la configuración — crea campañas o notifica al operador.
/// </summary>
public interface IDelinquencyProcessor
{
    /// <summary>
    /// Procesa el payload JSON recibido del webhook de descarga de morosidad.
    /// </summary>
    /// <param name="tenantId">Tenant propietario de la acción.</param>
    /// <param name="actionDefinitionId">Acción de morosidad que originó el payload.</param>
    /// <param name="jsonPayload">Respuesta cruda del webhook (array o objeto con array anidado).</param>
    /// <param name="scheduledJobId">Job que disparó la ejecución (null si es manual).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Id de la DelinquencyExecution creada.</returns>
    Task<Guid> ProcessAsync(
        Guid tenantId,
        Guid actionDefinitionId,
        string jsonPayload,
        Guid? scheduledJobId = null,
        CancellationToken ct = default);
}
