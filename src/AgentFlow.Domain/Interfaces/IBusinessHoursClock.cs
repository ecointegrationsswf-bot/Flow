using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Servicio que centraliza el cálculo de horario laboral en la zona horaria
/// del tenant. Lo usan tanto el dispatcher de campañas (al programar follow-ups)
/// como los executors (FollowUpExecutor) para decidir si están dentro de la
/// ventana de envío permitida.
///
/// Prioridad de configuración:
///   1. <c>CampaignTemplate.SendFrom/SendUntil</c>  (override por campaña)
///   2. <c>Tenant.BusinessHoursStart/End</c>        (default por tenant)
///
/// Días laborales:
///   - <c>CampaignTemplate.AttentionDays</c> si la lista no está vacía.
///   - Lunes a viernes en caso contrario.
/// </summary>
public interface IBusinessHoursClock
{
    /// <summary>
    /// True si <paramref name="utcInstant"/> cae dentro del horario laboral del
    /// tenant cuando se convierte a su TZ.
    /// </summary>
    bool IsWithinBusinessHours(DateTime utcInstant, Tenant tenant, CampaignTemplate? template = null);

    /// <summary>
    /// Devuelve el próximo instante UTC en el que el tenant entra en horario
    /// laboral, partiendo de <paramref name="utcAfter"/>. Si <paramref name="utcAfter"/>
    /// ya está dentro, lo retorna tal cual.
    /// </summary>
    DateTime NextBusinessWindowStartUtc(DateTime utcAfter, Tenant tenant, CampaignTemplate? template = null);

    /// <summary>
    /// Si <paramref name="utcCandidate"/> cae dentro de la ventana laboral, lo
    /// retorna sin cambios. Si cae antes, mueve a la apertura del mismo día.
    /// Si cae después o en día no laboral, mueve a la apertura del próximo día
    /// laboral.
    /// </summary>
    DateTime AlignToBusinessHoursUtc(DateTime utcCandidate, Tenant tenant, CampaignTemplate? template = null);
}
