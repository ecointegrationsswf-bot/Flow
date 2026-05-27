namespace AgentFlow.Application.Modules.Reports;

/// <summary>
/// Calcula el reporte de efectividad para un tenant en un rango de fechas, con
/// filtro opcional por <b>maestros de campaña</b> (CampaignTemplate). Filtrar
/// por maestro permite ver el desempeño consolidado de un mismo agente IA y
/// guion a lo largo de múltiples corridas (ej: todas las campañas de "Cobros
/// Somos" lanzadas en el último trimestre).
///
/// La implementación vive en Infrastructure (acceso a SQL Server con queries
/// optimizadas para agrupar por <c>ClientPhone</c>).
/// </summary>
public interface IEffectivenessReportService
{
    /// <summary>
    /// Genera el reporte. Si <paramref name="campaignTemplateIds"/> es null o vacío,
    /// incluye TODAS las campañas del tenant dentro del rango sin filtrar por maestro.
    /// </summary>
    Task<EffectivenessReportDto> GenerateAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<Guid>? campaignTemplateIds,
        CancellationToken ct);
}
