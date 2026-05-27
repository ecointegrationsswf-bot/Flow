using AgentFlow.Application.Modules.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgentFlow.Infrastructure.Reports;

/// <summary>
/// Genera el PDF del reporte de efectividad usando QuestPDF (no es screenshot —
/// es PDF nativo con tipografía vectorial, headers/footers reales, paginación
/// automática y soporte UTF-8 nativo).
///
/// Diseño visual:
///  - Portada con KPIs grandes en cards.
///  - Sección de filtros aplicados (rango + campañas seleccionadas).
///  - Tablas para distribución de contactos y distribución de resultados.
///  - Breakdown por campaña al final.
///  - Header con "Reporte de Efectividad — {tenant}" y footer con paginación.
///
/// Licencia: usamos Community Edition. La línea <c>QuestPDF.Settings.License</c>
/// se setea una sola vez al inicializar el servicio (en Program.cs).
/// </summary>
public class EffectivenessReportPdfGenerator
{
    private static readonly string NAVY = "#1A3A6B";
    private static readonly string GREEN = "#047857";
    private static readonly string AMBER = "#D97706";
    private static readonly string RED = "#B91C1C";
    private static readonly string GRAY = "#64748B";
    private static readonly string GRAY_BG = "#F1F5F9";
    private static readonly string GRAY_BORDER = "#CBD5E1";

    /// <summary>
    /// Genera el PDF. Si <paramref name="logoBytes"/> tiene valor, se renderiza
    /// el logo del tenant en el header (izquierda del título). El controller es
    /// responsable de descargar la imagen desde <c>report.Filters.TenantLogoUrl</c>
    /// — separamos download de render para que la generación sea CPU-bound puro
    /// y testeable sin red.
    /// </summary>
    public byte[] Generate(EffectivenessReportDto report, byte[]? logoBytes = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10).FontColor(Colors.Black));

                page.Header().Element(h => Header(h, report, logoBytes));
                page.Content().Element(c => Body(c, report));
                page.Footer().Element(Footer);
            });
        });

        return document.GeneratePdf();
    }

    // ── HEADER ────────────────────────────────────────────────────────────
    private static void Header(IContainer container, EffectivenessReportDto report, byte[]? logoBytes)
    {
        container.PaddingBottom(8).BorderBottom(1).BorderColor(NAVY).Row(row =>
        {
            // Logo del tenant (si está disponible). Lo dejamos a la izquierda,
            // con altura fija para no romper el layout cuando el logo es muy alto.
            // Si la descarga falló o el tenant no tiene logo configurado, simplemente
            // omitimos esta columna y el título queda a la izquierda como antes.
            if (logoBytes is { Length: > 0 })
            {
                row.ConstantItem(60).AlignMiddle().Height(40).Image(logoBytes).FitArea();
                row.ConstantItem(10); // separador visual entre logo y título
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Reporte de Efectividad — Campañas de Cobranza").FontSize(14).Bold().FontColor(NAVY);
                col.Item().Text(report.Filters.TenantName).FontSize(10).FontColor(GRAY);
            });
            row.ConstantItem(120).AlignRight().Text(report.Filters.GeneratedAtUtc.AddHours(-5).ToString("dd-MMM-yyyy HH:mm"))
                .FontSize(9).FontColor(GRAY);
        });
    }

    // ── BODY ──────────────────────────────────────────────────────────────
    private static void Body(IContainer container, EffectivenessReportDto report)
    {
        container.PaddingTop(12).Column(col =>
        {
            col.Spacing(14);

            // 1. Filtros aplicados
            col.Item().Element(c => FiltersBlock(c, report.Filters));

            // 2. KPIs principales (4 cards)
            col.Item().Element(c => KpiCards(c, report.Summary));

            // 3. Tabla — Distribución de contactos por cliente
            col.Item().PaddingTop(10).Element(c => Section(c, "Distribución de contactos por cliente"));
            col.Item().Element(c => ContactDistributionTable(c, report.ContactDistribution, report.Summary));

            // 4. Tabla — Resultado por cliente único
            col.Item().PaddingTop(6).Element(c => Section(c, "Resultado por cliente único"));
            col.Item().Element(c => ResultDistributionTable(c, report.ResultDistribution));

            // 5. Breakdown por campaña (si hay 2+ campañas)
            if (report.Campaigns.Count > 0)
            {
                col.Item().PaddingTop(6).Element(c => Section(c, "Detalle por campaña"));
                col.Item().Element(c => CampaignsTable(c, report.Campaigns));
            }

            // 6. Notas metodológicas
            col.Item().PaddingTop(8).Element(c => Methodology(c));
        });
    }

    private static void Section(IContainer container, string title) =>
        container.PaddingBottom(4).BorderBottom(1).BorderColor(GRAY_BORDER).Text(title).FontSize(12).Bold().FontColor(NAVY);

    // ── FILTROS ──────────────────────────────────────────────────────────
    private static void FiltersBlock(IContainer container, ReportFilters f)
    {
        container.Background(GRAY_BG).Padding(10).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(t => { t.Span("Rango: ").Bold(); t.Span($"{f.FromUtc.AddHours(-5):dd-MMM-yyyy} a {f.ToUtc.AddHours(-5):dd-MMM-yyyy}"); });
                col.Item().Text(t =>
                {
                    t.Span("Maestros de campaña: ").Bold();
                    t.Span(f.CampaignTemplateNames is { Count: > 0 }
                        ? string.Join(", ", f.CampaignTemplateNames.Take(3)) + (f.CampaignTemplateNames.Count > 3 ? $" + {f.CampaignTemplateNames.Count - 3} más" : "")
                        : "Todos los maestros del rango");
                });
            });
        });
    }

    // ── KPIs CARDS ───────────────────────────────────────────────────────
    private static void KpiCards(IContainer container, ReportSummary s)
    {
        container.Row(row =>
        {
            row.Spacing(8);
            row.RelativeItem().Element(c => Kpi(c, "Clientes únicos contactados", s.UniqueClients.ToString(), $"{s.TotalContactsSent} mensajes enviados", NAVY));
            row.RelativeItem().Element(c => Kpi(c, "Promedio contactos / cliente", s.AverageContactsPerClient.ToString("0.00"), "Saturación del contacto", AMBER));
            row.RelativeItem().Element(c => Kpi(c, "Engagement", $"{s.EngagementRate:0.0}%", $"{s.ClientsWhoResponded} clientes respondieron", GREEN));
            row.RelativeItem().Element(c => Kpi(c, "Gestión efectiva", $"{s.EffectiveManagementRate:0.0}%", $"{s.ClientsConfirmedPayment + s.ClientsWithPromise + s.ClientsNegotiating} de {s.UniqueClients} clientes", GREEN));
        });
    }

    private static void Kpi(IContainer container, string label, string value, string subtitle, string color)
    {
        container.Background(GRAY_BG).Border(1).BorderColor(GRAY_BORDER).Padding(10).Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(GRAY);
            col.Item().PaddingTop(4).Text(value).FontSize(22).Bold().FontColor(color);
            col.Item().PaddingTop(2).Text(subtitle).FontSize(8).FontColor(Colors.Black);
        });
    }

    // ── DISTRIBUTION TABLES ──────────────────────────────────────────────
    private static void ContactDistributionTable(IContainer container, ContactDistribution dist, ReportSummary summary)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
            });
            t.Header(h =>
            {
                h.Cell().Element(Th).Text("Veces contactado");
                h.Cell().Element(Th).Text("Clientes");
                h.Cell().Element(Th).Text("% del universo");
                h.Cell().Element(Th).Text("Mensajes enviados");
            });
            foreach (var b in dist.Buckets)
            {
                t.Cell().Element(Td).Text(b.ContactCount >= 4 ? "4+ veces" : $"{b.ContactCount} vez{(b.ContactCount == 1 ? "" : "es")}");
                t.Cell().Element(Td).AlignCenter().Text(b.Clients.ToString());
                t.Cell().Element(Td).AlignCenter().Text($"{b.PercentageOfUniqueClients:0.0}%");
                t.Cell().Element(Td).AlignCenter().Text((b.Clients * b.ContactCount).ToString());
            }
            t.Cell().Element(Tf).Text("TOTAL").Bold();
            t.Cell().Element(Tf).AlignCenter().Text(summary.UniqueClients.ToString()).Bold();
            t.Cell().Element(Tf).AlignCenter().Text("100.0%").Bold();
            t.Cell().Element(Tf).AlignCenter().Text(summary.TotalContactsSent.ToString()).Bold();
        });
    }

    private static void ResultDistributionTable(IContainer container, ResultDistribution dist)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(3);
            });
            t.Header(h =>
            {
                h.Cell().Element(Th).Text("Resultado final");
                h.Cell().Element(Th).Text("Clientes");
                h.Cell().Element(Th).Text("% del total");
                h.Cell().Element(Th).Text("Categoría");
            });
            foreach (var b in dist.Buckets)
            {
                string color = b.Category switch
                {
                    "Conversión"    => GREEN,
                    "Compromiso"    => GREEN,
                    "Negociación"   => "#0F172A",
                    "Disputa"       => AMBER,
                    "Cancelación"   => RED,
                    "Sin Respuesta" => GRAY,
                    _               => "#0F172A",
                };
                t.Cell().Element(Td).Text(b.Label).FontColor(color);
                t.Cell().Element(Td).AlignCenter().Text(b.Clients.ToString()).FontColor(color);
                t.Cell().Element(Td).AlignCenter().Text($"{b.Percentage:0.0}%").FontColor(color);
                t.Cell().Element(Td).Text(b.Category).FontColor(color);
            }
        });
    }

    private static void CampaignsTable(IContainer container, IReadOnlyList<CampaignBreakdown> campaigns)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
            });
            t.Header(h =>
            {
                h.Cell().Element(Th).Text("Campaña");
                h.Cell().Element(Th).Text("Lanzada");
                h.Cell().Element(Th).Text("Contactos");
                h.Cell().Element(Th).Text("Únicos");
                h.Cell().Element(Th).Text("Respondieron");
                h.Cell().Element(Th).Text("Pagaron");
            });
            foreach (var c in campaigns)
            {
                t.Cell().Element(Td).Text(c.CampaignName.Length > 30 ? c.CampaignName[..30] + "…" : c.CampaignName);
                t.Cell().Element(Td).AlignCenter().Text(c.LaunchedAtUtc.AddHours(-5).ToString("dd-MMM"));
                t.Cell().Element(Td).AlignCenter().Text(c.TotalContacts.ToString());
                t.Cell().Element(Td).AlignCenter().Text(c.UniqueClients.ToString());
                t.Cell().Element(Td).AlignCenter().Text(c.Responded.ToString());
                t.Cell().Element(Td).AlignCenter().Text(c.ConfirmedPayment.ToString()).FontColor(GREEN).Bold();
            }
        });
    }

    // ── METHODOLOGY ──────────────────────────────────────────────────────
    private static void Methodology(IContainer container)
    {
        container.PaddingTop(4).Padding(8).Background(GRAY_BG).Column(col =>
        {
            col.Item().Text("Nota metodológica").FontSize(10).Bold().FontColor(NAVY);
            col.Item().PaddingTop(2).Text("Todas las métricas se calculan sobre CLIENTES ÚNICOS (teléfono distinto), no por mensaje-evento. Un cliente contactado tres veces cuenta como una persona. El \"Resultado final\" toma la etiqueta de mayor valor estratégico para cada cliente: Confirmó Pago > Promesa de Pago > Negociación > Disputa > Sin Respuesta. Los porcentajes son sobre el total de clientes únicos contactados en el rango.").FontSize(8).FontColor(GRAY);
        });
    }

    // ── TABLE STYLES ─────────────────────────────────────────────────────
    private static IContainer Th(IContainer c) => c.Background(NAVY).Padding(6).DefaultTextStyle(s => s.FontColor(Colors.White).Bold().FontSize(9));
    private static IContainer Td(IContainer c) => c.BorderBottom(0.5f).BorderColor(GRAY_BORDER).Padding(6).DefaultTextStyle(s => s.FontSize(9));
    private static IContainer Tf(IContainer c) => c.BorderTop(1).BorderColor(NAVY).Background(GRAY_BG).Padding(6).DefaultTextStyle(s => s.FontSize(10));

    // ── FOOTER ───────────────────────────────────────────────────────────
    private static void Footer(IContainer container)
    {
        container.PaddingTop(8).BorderTop(0.5f).BorderColor(GRAY_BORDER).Row(row =>
        {
            row.RelativeItem().Text("Reporte generado por TalkIA — métricas por cliente único.")
                .FontSize(8).FontColor(GRAY);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(GRAY));
                t.Span("Página "); t.CurrentPageNumber(); t.Span(" de "); t.TotalPages();
            });
        });
    }
}
