using AgentFlow.Application.Modules.Dashboard;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgentFlow.Infrastructure.Reports;

/// <summary>
/// Genera el Informe Gerencial como PDF nativo (no screenshot). El gráfico de
/// barras apiladas se renderiza server-side a PNG (vía System.Drawing en
/// DashboardController) y se embebe como imagen. El resto del PDF (header,
/// tabla cruzada, hallazgos) se compone con QuestPDF puro.
///
/// Diseño visual fiel a la vista en pantalla:
///  - Página 1: header con logo + título azul + meta del período + chart + nota
///  - Página 2: tabla cruzada períodos × etiquetas + Resultados clave
///  - Letter landscape (igual que el @page CSS de impresión)
/// </summary>
public class ManagementReportPdfGenerator
{
    private const string NAVY = "#1d4ed8";       // azul título (text-blue-700)
    private const string BLUE_LIGHT = "#dbeafe"; // header de tabla (bg-blue-100)
    private const string BLUE_BG = "#eff6ff";    // grand total row
    private const string GRAY_BORDER = "#dbeafe";
    private const string GRAY = "#6b7280";
    private const string TEXT_DARK = "#0f172a";
    private const string UNLABELED_HEX = "#cbd5e1";
    private const string UNLABELED_NAME = "Sin etiqueta";

    /// <summary>
    /// Genera el PDF. <paramref name="chartPng"/> es el PNG ya renderizado del
    /// gráfico apilado (mismo render que el embebido en el Excel). Si llega null
    /// se omite el gráfico — el resto del documento se genera igual.
    /// <paramref name="logoBytes"/> es el logo del tenant para el header.
    /// </summary>
    public byte[] Generate(ManagementReportDto report, byte[]? chartPng, byte[]? logoBytes)
    {
        var document = Document.Create(container =>
        {
            // ── Página 1: portada + gráfico ─────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.Letter.Landscape());
                page.Margin(34);  // ~12mm como el @page CSS
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10).FontColor(TEXT_DARK));

                page.Content().Column(col =>
                {
                    col.Item().Element(c => HeroHeader(c, report, logoBytes, isPageOne: true));
                    col.Item().PaddingTop(8).Element(c => ChartBlock(c, report, chartPng));
                    col.Item().PaddingTop(10).Element(MethodologyNote);
                });

                page.Footer().Element(c => Footer(c, "Página 1 de 2"));
            });

            // ── Página 2: tabla + hallazgos ─────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.Letter.Landscape());
                page.Margin(34);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10).FontColor(TEXT_DARK));

                page.Content().Column(col =>
                {
                    col.Item().Element(c => HeroHeader(c, report, logoBytes, isPageOne: false));
                    col.Item().PaddingTop(8).Element(c => PeriodsTable(c, report));
                    col.Item().PaddingTop(12).Element(c => KeyFindingsBlock(c, report));
                });

                page.Footer().Element(c => Footer(c, "Página 2 de 2"));
            });
        });

        return document.GeneratePdf();
    }

    // ── HEADER (compartido) ──────────────────────────────────────────────
    private static void HeroHeader(IContainer container, ManagementReportDto r, byte[]? logoBytes, bool isPageOne)
    {
        container.PaddingBottom(8).BorderBottom(1).BorderColor(GRAY_BORDER).Row(row =>
        {
            if (logoBytes is { Length: > 0 })
            {
                row.ConstantItem(70).AlignMiddle().Height(50).Image(logoBytes).FitArea();
                row.ConstantItem(10);
            }

            row.RelativeItem().Column(col =>
            {
                var titlePrefix = isPageOne ? "Resumen de Resultados" : "Detalle por período";
                col.Item().Text($"{titlePrefix} — {r.TenantName}")
                    .FontSize(20).Bold().FontColor(NAVY);

                col.Item().PaddingTop(2).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(9).FontColor(GRAY));
                    t.Span("Período: ");
                    t.Span($"{FormatDate(r.From)} a {FormatDate(r.To)}");
                    t.Span(" · Granularidad: ");
                    t.Span(r.Granularity == "monthly" ? "Mensual" : "Quincenal");
                    if (!string.IsNullOrWhiteSpace(r.CampaignTemplateName))
                    {
                        t.Span(" · Maestro: ");
                        t.Span(r.CampaignTemplateName!).Bold();
                    }
                    t.Span($" · Generado el {DateTime.UtcNow.AddHours(-5):dd/MM/yyyy HH:mm} (Panamá)");
                });
            });
        });
    }

    // ── CHART (PNG embebido) ─────────────────────────────────────────────
    private static void ChartBlock(IContainer container, ManagementReportDto r, byte[]? chartPng)
    {
        if (chartPng is { Length: > 0 })
        {
            container.AlignCenter().MaxWidth(720).Image(chartPng).FitWidth();
            return;
        }

        // Fallback: si el chart PNG no se pudo generar (GDI+ no disponible),
        // imprimimos al menos una tabla textual con efectividad por período.
        if (r.Periods.Count == 0)
        {
            container.Text("No hay períodos para mostrar.").FontSize(11).Italic().FontColor(GRAY);
            return;
        }
        container.Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); });
            t.Header(h =>
            {
                h.Cell().Element(ThHeader).Text("Período");
                h.Cell().Element(ThHeader).Text("Total");
                h.Cell().Element(ThHeader).Text("Efectividad");
            });
            foreach (var p in r.Periods)
            {
                t.Cell().Element(Td).Text(p.Label);
                t.Cell().Element(Td).AlignCenter().Text(p.Total.ToString());
                t.Cell().Element(Td).AlignCenter().Text($"{p.Effectiveness:0.0}%").Bold();
            }
        });
    }

    // ── METHODOLOGY NOTE ─────────────────────────────────────────────────
    private static void MethodologyNote(IContainer container)
    {
        container.Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(9).Italic().FontColor(GRAY));
            t.Span("Nota metodológica: ").Bold().Italic(false).FontColor(TEXT_DARK);
            t.Span("Este informe mide el ");
            t.Span("rendimiento de las campañas").Bold().Italic(false).FontColor(TEXT_DARK);
            t.Span(". El universo de cada período son los teléfonos únicos contactados por campañas creadas en ese período (");
            t.Span("CampaignContacts").FontFamily("Consolas").FontSize(8);
            t.Span("). Conversaciones espontáneas sin campaña asociada no entran. Una respuesta tardía sigue contando — el cliente cae en el período de SU campaña original, no en el mes en que respondió. A cada cliente se le asigna su mejor resultado por rank: Confimó Pago › Promesa › Negociación › Disputa › Cancelación. Efectividad = clientes con mejor etiqueta en {Confimó Pago, Promesa, Negociación}. Estas métricas coinciden con el Informe de Efectividad sobre el mismo rango.");
        });
    }

    // ── TABLA CRUZADA períodos × etiquetas ───────────────────────────────
    private static void PeriodsTable(IContainer container, ManagementReportDto r)
    {
        // Etiquetas únicas presentes en cualquier período (orden estable).
        var allLabels = r.Periods
            .SelectMany(p => p.Breakdown)
            .GroupBy(b => b.LabelId)
            .Select(g => g.First())
            .ToList();

        var periodCount = r.Periods.Count;

        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);  // clasificación
                for (int i = 0; i < periodCount; i++)
                {
                    c.RelativeColumn(2);  // cantidad
                    c.RelativeColumn(2);  // porcentaje
                }
                c.RelativeColumn(2);     // total cantidad
                c.RelativeColumn(2);     // total %
            });

            // Header fila 1 — agrupado por período
            t.Header(h =>
            {
                h.Cell().Element(ThHeader).Text("Clasificación");
                foreach (var p in r.Periods)
                {
                    h.Cell().ColumnSpan(2).Element(ThHeader).AlignCenter().Text(p.Label);
                }
                h.Cell().ColumnSpan(2).Element(ThHeader).AlignCenter().Text("Total");

                // Header fila 2 — Cant / %
                h.Cell().Element(ThSub).Text("");
                for (int i = 0; i < periodCount; i++)
                {
                    h.Cell().Element(ThSub).AlignRight().Text("Cant.");
                    h.Cell().Element(ThSub).AlignRight().Text("%");
                }
                h.Cell().Element(ThSub).AlignRight().Text("Cant.");
                h.Cell().Element(ThSub).AlignRight().Text("%");
            });

            int totalAll = r.TotalAll;

            foreach (var l in allLabels)
            {
                int rowTotalCount = r.Periods.Sum(p => p.Breakdown.FirstOrDefault(b => b.LabelId == l.LabelId)?.Count ?? 0);
                double rowTotalPct = totalAll == 0 ? 0 : Math.Round(rowTotalCount * 100.0 / totalAll, 1);

                // Clasificación con punto de color
                t.Cell().Element(Td).Row(row =>
                {
                    row.ConstantItem(10).Height(8).Background(l.Color);
                    row.ConstantItem(4);
                    row.RelativeItem().Text(text =>
                    {
                        text.Span(l.Name);
                        if (l.IsEffective)
                            text.Span("  ✓ efectiva").FontSize(7).FontColor("#047857");
                    });
                });

                foreach (var p in r.Periods)
                {
                    var b = p.Breakdown.FirstOrDefault(x => x.LabelId == l.LabelId);
                    t.Cell().Element(Td).AlignRight().Text((b?.Count ?? 0).ToString());
                    var pctText = b is null ? "0%" : $"{b.Percentage:0.0}%";
                    if (l.IsEffective)
                        t.Cell().Element(Td).AlignRight().Text(pctText).FontColor(l.Color).Bold();
                    else
                        t.Cell().Element(Td).AlignRight().Text(pctText).FontColor(GRAY);
                }

                t.Cell().Element(Td).AlignRight().Text(rowTotalCount.ToString()).Bold();
                t.Cell().Element(Td).AlignRight().Text($"{rowTotalPct:0.0}%");
            }

            // Fila "Sin etiqueta" si hay algún período con sin-etiqueta > 0
            if (r.Periods.Any(p => p.UnlabeledCount > 0))
            {
                int totalUn = r.Periods.Sum(p => p.UnlabeledCount);
                double pctUn = totalAll == 0 ? 0 : Math.Round(totalUn * 100.0 / totalAll, 1);

                t.Cell().Element(Td).Row(row =>
                {
                    row.ConstantItem(10).Height(8).Background(UNLABELED_HEX);
                    row.ConstantItem(4);
                    row.RelativeItem().Text(UNLABELED_NAME).Italic().FontColor(GRAY);
                });
                foreach (var p in r.Periods)
                {
                    t.Cell().Element(Td).AlignRight().Text(p.UnlabeledCount.ToString()).FontColor(GRAY);
                    t.Cell().Element(Td).AlignRight().Text($"{p.UnlabeledPercentage:0.0}%").FontColor(GRAY);
                }
                t.Cell().Element(Td).AlignRight().Text(totalUn.ToString()).Bold().FontColor(GRAY);
                t.Cell().Element(Td).AlignRight().Text($"{pctUn:0.0}%").FontColor(GRAY);
            }

            // Grand Total
            t.Cell().Element(GrandTotalCell).Text("Grand Total").Bold().FontColor(NAVY);
            foreach (var p in r.Periods)
            {
                t.Cell().ColumnSpan(2).Element(GrandTotalCell).AlignCenter()
                    .Text($"{p.Total}  ({p.Effectiveness:0.0}% efec.)").Bold().FontColor(NAVY);
            }
            t.Cell().ColumnSpan(2).Element(GrandTotalCell).AlignCenter()
                .Text($"{totalAll}  ({r.EffectivenessAll:0.0}% efec.)").Bold().FontColor(NAVY);
        });
    }

    // ── HALLAZGOS CLAVE ──────────────────────────────────────────────────
    private static void KeyFindingsBlock(IContainer container, ManagementReportDto r)
    {
        var findings = BuildKeyFindings(r);
        container.Column(col =>
        {
            col.Item().Text("RESULTADOS CLAVE").FontSize(11).Bold().FontColor(NAVY);
            col.Item().PaddingTop(4).Column(inner =>
            {
                foreach (var f in findings)
                {
                    inner.Item().Row(row =>
                    {
                        row.ConstantItem(12).Text("•").FontColor(NAVY);
                        row.RelativeItem().Text(f).FontSize(10);
                    });
                }
            });
        });
    }

    private static List<string> BuildKeyFindings(ManagementReportDto r)
    {
        var list = new List<string>();
        if (r.Periods.Count == 0)
        {
            list.Add("No hay datos en el período seleccionado.");
            return list;
        }

        // 1) Volumen total
        list.Add(
            $"Se gestionaron {r.TotalAll} clientes únicos en el rango completo, con una efectividad agregada del {r.EffectivenessAll:0.0}%.");

        // 2) Etiqueta dominante en el último período
        var last = r.Periods[^1];
        if (last.Breakdown.Count > 0)
        {
            var top = last.Breakdown.OrderByDescending(b => b.Count).First();
            list.Add($"En \"{last.Label}\" la clasificación de mayor volumen fue \"{top.Name}\" con {top.Count} casos.");
        }

        // 3) Tendencia de efectividad
        if (r.Periods.Count >= 2)
        {
            var first = r.Periods[0];
            var diff = last.Effectiveness - first.Effectiveness;
            if (Math.Abs(diff) >= 1)
            {
                var verb = diff > 0 ? "aumentó" : "disminuyó";
                list.Add($"La efectividad {verb} {Math.Abs(diff):0.0} puntos porcentuales entre \"{first.Label}\" y \"{last.Label}\".");
            }
        }

        // 4) Etiqueta efectiva con mejor desempeño global
        var effective = r.Periods
            .SelectMany(p => p.Breakdown)
            .Where(b => b.IsEffective)
            .GroupBy(b => new { b.LabelId, b.Name })
            .Select(g => new { g.Key.Name, Total = g.Sum(x => x.Count) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();
        if (effective is not null && effective.Total > 0)
        {
            list.Add($"La etiqueta efectiva con mayor volumen fue \"{effective.Name}\" con {effective.Total} casos.");
        }

        // 5) Sin respuesta
        var sinResp = r.Periods
            .SelectMany(p => p.Breakdown)
            .GroupBy(b => new { b.LabelId, b.Name })
            .Where(g => System.Text.RegularExpressions.Regex.IsMatch(g.Key.Name, "sin\\s*respuesta|no\\s*respondi", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Select(g => new { g.Key.Name, Total = g.Sum(x => x.Count) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();
        if (sinResp is not null && sinResp.Total > 0)
        {
            var pct = r.TotalAll == 0 ? 0 : Math.Round(sinResp.Total * 100.0 / r.TotalAll);
            list.Add($"La categoría \"{sinResp.Name}\" representó el {pct}% del total.");
        }

        return list;
    }

    // ── FOOTER ───────────────────────────────────────────────────────────
    private static void Footer(IContainer container, string pageLabel)
    {
        container.PaddingTop(8).BorderTop(0.5f).BorderColor(GRAY_BORDER).Row(row =>
        {
            row.RelativeItem().Text("TalkIA · Informe Gerencial").FontSize(8).FontColor(GRAY);
            row.ConstantItem(80).AlignRight().Text(pageLabel).FontSize(8).FontColor(GRAY);
        });
    }

    // ── STYLES ───────────────────────────────────────────────────────────
    private static IContainer ThHeader(IContainer c) =>
        c.Background(BLUE_LIGHT).Padding(5).DefaultTextStyle(s => s.FontColor(NAVY).Bold().FontSize(9));
    private static IContainer ThSub(IContainer c) =>
        c.Background("#f1f5f9").Padding(3).DefaultTextStyle(s => s.FontColor(GRAY).FontSize(7).Italic());
    private static IContainer Td(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor("#e5e7eb").Padding(4).DefaultTextStyle(s => s.FontSize(8));
    private static IContainer GrandTotalCell(IContainer c) =>
        c.Background(BLUE_BG).BorderTop(1).BorderColor("#bfdbfe").Padding(5).DefaultTextStyle(s => s.FontSize(9));

    private static string FormatDate(string yyyymmdd)
    {
        // El DTO trae fechas como yyyy-MM-dd; en pantalla las mostramos dd/MM/yyyy.
        var parts = yyyymmdd.Split('-');
        if (parts.Length == 3) return $"{parts[2]}/{parts[1]}/{parts[0]}";
        return yyyymmdd;
    }
}
