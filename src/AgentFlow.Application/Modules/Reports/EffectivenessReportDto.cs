namespace AgentFlow.Application.Modules.Reports;

/// <summary>
/// Reporte de efectividad de campañas por cliente único en un rango de fechas.
///
/// Diferencia con un reporte simple por evento: agrupa por <c>ClientPhone</c>
/// y calcula KPIs sobre PERSONAS (no eventos). Permite al gerente de cobranzas
/// medir cuántos clientes realmente terminaron en estado positivo, no cuántos
/// mensajes salieron.
///
/// Se sirve en dos formatos:
///  - JSON (vista previa en la UI antes de exportar)
///  - PDF (descarga, generado con QuestPDF — tipografía profesional)
/// </summary>
public record EffectivenessReportDto(
    ReportFilters Filters,
    ReportSummary Summary,
    ContactDistribution ContactDistribution,
    ResultDistribution ResultDistribution,
    IReadOnlyList<CampaignBreakdown> Campaigns);

/// <summary>Filtros aplicados al reporte. Se imprimen en la portada del PDF para auditoría.</summary>
public record ReportFilters(
    DateTime FromUtc,
    DateTime ToUtc,
    string TenantName,
    IReadOnlyList<string>? CampaignTemplateNames,  // null = todos los maestros del rango (= todas las campañas)
    DateTime GeneratedAtUtc,
    string? TenantLogoUrl);                        // URL pública del logo del tenant — render en header del PDF

/// <summary>KPIs principales — los números grandes del resumen ejecutivo.</summary>
public record ReportSummary(
    int TotalCampaigns,
    int TotalContactsSent,              // suma de CampaignContacts (evento)
    int UniqueClients,                  // teléfonos distintos
    decimal AverageContactsPerClient,   // TotalContacts / UniqueClients
    int ClientsWhoResponded,            // únicos con al menos 1 Inbound
    decimal EngagementRate,             // ClientsWhoResponded / UniqueClients × 100
    int ClientsConfirmedPayment,        // únicos con label "Confimo Pago"
    decimal ConfirmedPaymentRate,       // / UniqueClients × 100
    int ClientsWithPromise,             // únicos con label "Promesa de Pago"
    int ClientsNegotiating,             // únicos con label "Negociación / Acuerdo"
    int ClientsWithDispute,             // únicos con label "Disputa / Reclamo"
    int ClientsRequestingCancel,        // únicos con label "Solicita Cancelación"
    int ClientsSilent,                  // únicos sin label positiva (Sin Respuesta)
    decimal EffectiveManagementRate);   // (Confirmed + Promise + Negotiating) / Unique × 100

/// <summary>Cuántas veces fue contactado cada cliente (1, 2, 3+ veces).</summary>
public record ContactDistribution(
    IReadOnlyList<ContactDistributionBucket> Buckets);

public record ContactDistributionBucket(
    int ContactCount,    // 1, 2, 3, 4+
    int Clients,
    decimal PercentageOfUniqueClients);

/// <summary>Distribución del MEJOR resultado por cliente único.</summary>
public record ResultDistribution(
    IReadOnlyList<ResultDistributionBucket> Buckets);

public record ResultDistributionBucket(
    string Label,             // "Confirmó Pago", "Promesa de Pago", etc.
    int Clients,
    decimal Percentage,
    string Category);         // "Conversión", "Compromiso", "Negociación", "Disputa", "Sin Respuesta"

/// <summary>Detalle por campaña individual (útil cuando se filtran varias).</summary>
public record CampaignBreakdown(
    Guid CampaignId,
    string CampaignName,
    DateTime LaunchedAtUtc,
    int TotalContacts,
    int UniqueClients,
    int Responded,
    int ConfirmedPayment);
