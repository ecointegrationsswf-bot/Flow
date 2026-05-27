namespace AgentFlow.Application.Modules.Dashboard;

/// <summary>
/// DTO fuertemente tipado del Informe Gerencial. Comparte el shape (camelCase
/// vía System.Text.Json) con el endpoint <c>GET /api/dashboard/management-report</c>
/// y se reutiliza para el endpoint nuevo de PDF nativo. La serialización JSON
/// queda byte-equivalente a la del retorno anonymous-typed original.
/// </summary>
public record ManagementReportDto(
    string TenantName,
    string From,                       // yyyy-MM-dd
    string To,                         // yyyy-MM-dd
    string Granularity,                // "monthly" | "biweekly"
    Guid? CampaignTemplateId,
    string? CampaignTemplateName,
    IReadOnlyList<Guid> EffectiveLabelIds,
    int TotalAll,
    double EffectivenessAll,
    IReadOnlyList<ManagementReportPeriod> Periods);

public record ManagementReportPeriod(
    string Label,
    DateTime Start,
    DateTime End,
    int Total,
    double Effectiveness,
    int UnlabeledCount,
    double UnlabeledPercentage,
    IReadOnlyList<ManagementReportLabelBreakdown> Breakdown);

public record ManagementReportLabelBreakdown(
    Guid LabelId,
    string Name,
    string Color,
    bool IsEffective,
    int Count,
    double Percentage);
