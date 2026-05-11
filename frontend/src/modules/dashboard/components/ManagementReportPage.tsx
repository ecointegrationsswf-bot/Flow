import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowLeft, Printer, RefreshCcw, FileText, Loader2, FileDown } from 'lucide-react'
import {
  useManagementReport,
  useExportManagementReport,
  type ManagementReport,
  type ReportPeriod,
  type ReportLabelBreakdown,
} from '@/shared/hooks/useDashboard'
import { useCampaignTemplates } from '@/shared/hooks/useCampaignTemplates'
import { useToast, ToastContainer } from '@/shared/components/Toast'

const UNLABELED_COLOR = '#cbd5e1'
const UNLABELED_NAME = 'Sin etiqueta'

// Defaults: últimos 3 meses
function defaultRange(): { from: string; to: string } {
  const now = new Date()
  const panama = new Date(now.getTime() - 5 * 60 * 60 * 1000)
  const end = new Date(Date.UTC(panama.getUTCFullYear(), panama.getUTCMonth() + 1, 0))
  const start = new Date(Date.UTC(panama.getUTCFullYear(), panama.getUTCMonth() - 2, 1))
  const fmt = (d: Date) =>
    `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}-${String(d.getUTCDate()).padStart(2, '0')}`
  return { from: fmt(start), to: fmt(end) }
}

export function ManagementReportPage() {
  const initial = useMemo(defaultRange, [])
  const [from, setFrom] = useState(initial.from)
  const [to, setTo] = useState(initial.to)
  const [granularity, setGranularity] = useState<'monthly' | 'biweekly'>('monthly')
  const [campaignTemplateId, setCampaignTemplateId] = useState<string>('')
  const [generated, setGenerated] = useState(false)

  const { data: templates } = useCampaignTemplates()
  const { data, isLoading, isError, refetch, isFetching } = useManagementReport(
    { from, to, granularity, campaignTemplateId: campaignTemplateId || null },
    generated,
  )
  const exportMut = useExportManagementReport()
  const { toasts, remove, toast } = useToast()

  const handleGenerate = () => {
    setGenerated(true)
    // si ya estaba generado y solo cambió fechas/granularidad, refetch
    setTimeout(() => refetch(), 0)
  }

  const handlePrint = () => window.print()

  const handleExport = async () => {
    try {
      await exportMut.mutateAsync({
        from, to, granularity,
        campaignTemplateId: campaignTemplateId || null,
      })
      toast.success('Excel descargado.')
    } catch (err: unknown) {
      const e = err as { message?: string }
      toast.error(e.message ?? 'No se pudo exportar.')
    }
  }

  return (
    <div className="p-6 print:p-0">
      {/* ── Controles (ocultos al imprimir) ── */}
      <div className="mb-6 print:hidden">
        <Link to="/dashboard" className="inline-flex items-center gap-1 text-sm text-blue-600 hover:underline mb-3">
          <ArrowLeft className="h-4 w-4" /> Volver al Dashboard
        </Link>

        <div className="flex items-start justify-between gap-4 mb-4">
          <div>
            <h1 className="text-2xl font-semibold text-gray-900 flex items-center gap-2">
              <FileText className="h-6 w-6 text-blue-600" />
              Informe Gerencial
            </h1>
            <p className="text-sm text-gray-500 mt-1">
              Resumen comparativo de gestión por período. Imprimible / exportable a PDF.
            </p>
          </div>
          {generated && (
            <div className="flex items-center gap-2">
              <button
                onClick={handleExport}
                disabled={isLoading || isError || exportMut.isPending}
                className="inline-flex items-center gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-medium text-emerald-700 hover:bg-emerald-100 disabled:opacity-50"
              >
                {exportMut.isPending
                  ? <Loader2 className="h-4 w-4 animate-spin" />
                  : <FileDown className="h-4 w-4" />}
                Exportar Excel
              </button>
              <button
                onClick={handlePrint}
                disabled={isLoading || isError}
                className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                <Printer className="h-4 w-4" />
                Imprimir / Guardar PDF
              </button>
            </div>
          )}
        </div>

        <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
          <div className="flex flex-wrap items-end gap-3">
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Desde</label>
              <input
                type="date"
                value={from}
                onChange={(e) => setFrom(e.target.value)}
                className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Hasta</label>
              <input
                type="date"
                value={to}
                onChange={(e) => setTo(e.target.value)}
                className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Granularidad</label>
              <select
                value={granularity}
                onChange={(e) => setGranularity(e.target.value as 'monthly' | 'biweekly')}
                className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
              >
                <option value="monthly">Mensual</option>
                <option value="biweekly">Quincenal (Q1 / Q2)</option>
              </select>
            </div>
            <div className="min-w-[200px]">
              <label className="block text-xs font-medium text-gray-700 mb-1">Maestro de campaña</label>
              <select
                value={campaignTemplateId}
                onChange={(e) => setCampaignTemplateId(e.target.value)}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
                title="Filtra conversaciones por el maestro de campaña y muestra solo sus etiquetas asociadas."
              >
                <option value="">Todos (etiquetas del tenant)</option>
                {(templates ?? []).map((t) => (
                  <option key={t.id} value={t.id}>{t.name}</option>
                ))}
              </select>
            </div>
            <button
              onClick={handleGenerate}
              disabled={isFetching}
              className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 flex items-center gap-1"
            >
              {isFetching
                ? <Loader2 className="h-4 w-4 animate-spin" />
                : <RefreshCcw className="h-4 w-4" />}
              {generated ? 'Regenerar' : 'Generar informe'}
            </button>
          </div>
        </div>
      </div>

      {/* ── Informe (visible en pantalla y al imprimir) ── */}
      {!generated ? (
        <div className="rounded-lg border border-dashed border-gray-200 bg-white p-12 text-center print:hidden">
          <FileText className="mx-auto h-10 w-10 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">
            Selecciona el período y granularidad, luego presiona "Generar informe".
          </p>
        </div>
      ) : isLoading ? (
        <div className="flex items-center justify-center py-16 text-gray-400 print:hidden">
          <Loader2 className="h-6 w-6 animate-spin mr-2" /> Generando informe…
        </div>
      ) : isError ? (
        <div className="rounded-lg bg-red-50 p-6 text-center text-sm text-red-600 print:hidden">
          Error al generar el informe.
        </div>
      ) : data ? (
        <ReportContent data={data} />
      ) : null}

      {/* Estilos de impresión: landscape Letter, sin headers del navegador */}
      <style>{`
        @media print {
          @page { size: Letter landscape; margin: 12mm; }
          body { background: white !important; }
          .no-print { display: none !important; }
          .print-page { page-break-after: always; }
          .print-page:last-child { page-break-after: auto; }
        }
      `}</style>

      <ToastContainer toasts={toasts} onRemove={remove} />
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────────
// Contenido del informe (2 páginas)
// ────────────────────────────────────────────────────────────────────────────────
function ReportContent({ data }: { data: ManagementReport }) {
  // Universo de etiquetas presentes en TODOS los períodos (para columnas de tabla
  // y orden estable en la barra apilada)
  const allLabels = useMemo(() => {
    const map = new Map<string, { name: string; color: string; isEffective: boolean }>()
    for (const p of data.periods) {
      for (const b of p.breakdown) {
        if (!map.has(b.labelId)) {
          map.set(b.labelId, { name: b.name, color: b.color, isEffective: b.isEffective })
        }
      }
    }
    return Array.from(map.entries()).map(([labelId, info]) => ({ labelId, ...info }))
  }, [data])

  const printedAt = useMemo(() => {
    const now = new Date()
    const pa = new Date(now.getTime() - 5 * 60 * 60 * 1000)
    const fmt = (d: Date) =>
      `${String(d.getUTCDate()).padStart(2, '0')}/${String(d.getUTCMonth() + 1).padStart(2, '0')}/${d.getUTCFullYear()}` +
      ` ${String(d.getUTCHours()).padStart(2, '0')}:${String(d.getUTCMinutes()).padStart(2, '0')}`
    return fmt(pa)
  }, [])

  return (
    <div className="mx-auto max-w-[1100px] space-y-6">

      {/* ── Página 1 — Gráfico ── */}
      <section className="print-page bg-white rounded-lg p-8 shadow-sm print:shadow-none print:rounded-none">
        <header className="mb-6 border-b border-blue-100 pb-4">
          <h1 className="text-3xl font-bold text-blue-700 tracking-tight">
            Resumen de Resultados — {data.tenantName}
          </h1>
          <p className="text-sm text-gray-500 mt-1">
            Período: {formatDate(data.from)} a {formatDate(data.to)}
            {' · '}
            Granularidad: {data.granularity === 'monthly' ? 'Mensual' : 'Quincenal'}
            {data.campaignTemplateName && (
              <>
                {' · '}
                Maestro: <strong className="text-gray-700">{data.campaignTemplateName}</strong>
              </>
            )}
            {' · '}
            Generado el {printedAt} (Panamá)
          </p>
        </header>

        <StackedBarChart periods={data.periods} labels={allLabels} />

        <p className="mt-6 text-xs italic text-gray-500">
          <strong className="not-italic text-gray-700">Nota:</strong> Efectividad calculada como la suma de
          conversaciones clasificadas con etiquetas positivas
          {' '}<em>(compromisos de pago + envío de comprobantes + pagos confirmados)</em>{' '}
          sobre el total de conversaciones del período.
        </p>

        <footer className="mt-10 flex items-center justify-between text-xs text-gray-400">
          <span>TalkIA · Informe Gerencial</span>
          <span>Página 1 de 2</span>
        </footer>
      </section>

      {/* ── Página 2 — Tabla + análisis + recomendaciones ── */}
      <section className="print-page bg-white rounded-lg p-8 shadow-sm print:shadow-none print:rounded-none">
        <header className="mb-6 border-b border-blue-100 pb-4">
          <h2 className="text-2xl font-bold text-blue-700 tracking-tight">
            Detalle por período — {data.tenantName}
          </h2>
        </header>

        {/* Tabla cruzada */}
        <PeriodsTable periods={data.periods} labels={allLabels} totalAll={data.totalAll} effectivenessAll={data.effectivenessAll} />

        {/* Resultados clave (auto) */}
        <div className="mt-8">
          <h3 className="text-sm font-bold text-blue-700 uppercase tracking-wide mb-2">
            Resultados clave
          </h3>
          <ul className="list-disc pl-5 space-y-1.5 text-sm text-gray-700">
            {keyFindings(data, allLabels).map((finding, i) => (
              <li key={i}>{finding}</li>
            ))}
          </ul>
        </div>

        <footer className="mt-10 flex items-center justify-between text-xs text-gray-400">
          <span>TalkIA · Informe Gerencial</span>
          <span>Página 2 de 2</span>
        </footer>
      </section>
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────────
// Gráfico de barras horizontales 100% stacked en SVG manual
// ────────────────────────────────────────────────────────────────────────────────
function StackedBarChart({
  periods, labels,
}: {
  periods: ReportPeriod[]
  labels: { labelId: string; name: string; color: string; isEffective: boolean }[]
}) {
  const barHeight = 36
  const gap = 28
  const leftLabel = 140    // espacio para nombre del período
  const rightPadding = 10
  const width = 980
  const chartLeft = leftLabel
  const chartWidth = width - leftLabel - rightPadding
  const ticks = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100]
  const totalHeight = periods.length * (barHeight + gap) + 70

  if (periods.length === 0) {
    return <p className="text-sm text-gray-400 italic">No hay períodos para mostrar.</p>
  }

  return (
    <div className="overflow-x-auto">
      <svg width={width} height={totalHeight} viewBox={`0 0 ${width} ${totalHeight}`} className="text-gray-700">
        {/* Eje X — ticks */}
        {ticks.map((t) => {
          const x = chartLeft + (t / 100) * chartWidth
          return (
            <g key={t}>
              <line
                x1={x} y1={20} x2={x} y2={totalHeight - 40}
                stroke="#e5e7eb" strokeDasharray={t === 0 || t === 100 ? '0' : '2 3'}
              />
              <text x={x} y={totalHeight - 22} textAnchor="middle" fontSize="10" fill="#9ca3af">
                {t}%
              </text>
            </g>
          )
        })}

        {/* Barras */}
        {periods.map((p, idx) => {
          const y = 30 + idx * (barHeight + gap)
          let xCursor = chartLeft
          const segments = [
            ...labels.map((l) => {
              const b = p.breakdown.find((x) => x.labelId === l.labelId)
              return { name: l.name, color: l.color, pct: b?.percentage ?? 0 }
            }),
          ]
          // Sin etiqueta al final
          if (p.unlabeledPercentage > 0) {
            segments.push({ name: UNLABELED_NAME, color: UNLABELED_COLOR, pct: p.unlabeledPercentage })
          }

          return (
            <g key={idx}>
              {/* Label del período */}
              <text
                x={chartLeft - 10} y={y + barHeight / 2 + 4}
                textAnchor="end" fontSize="13" fontWeight="600" fill="#374151"
              >
                {p.label.split(' — ').map((line, i) => (
                  <tspan key={i} x={chartLeft - 10} dy={i === 0 ? 0 : 14}>{line}</tspan>
                ))}
              </text>

              {/* Efectividad arriba de la barra */}
              <text
                x={chartLeft + 4} y={y - 6}
                fontSize="12" fontWeight="700" fill="#0f172a"
              >
                Efectividad {p.effectiveness}%
              </text>

              {/* Segmentos apilados */}
              {segments.map((seg, si) => {
                if (seg.pct === 0) return null
                const w = (seg.pct / 100) * chartWidth
                const x = xCursor
                xCursor += w
                const showLabel = seg.pct >= 4
                return (
                  <g key={si}>
                    <rect
                      x={x} y={y} width={w} height={barHeight}
                      fill={seg.color}
                    />
                    {showLabel && (
                      <text
                        x={x + w / 2} y={y + barHeight / 2 + 4}
                        textAnchor="middle" fontSize="11" fontWeight="600"
                        fill={isDark(seg.color) ? '#ffffff' : '#0f172a'}
                      >
                        {Math.round(seg.pct)}%
                      </text>
                    )}
                  </g>
                )
              })}
            </g>
          )
        })}
      </svg>

      {/* Leyenda */}
      <div className="mt-4 flex flex-wrap gap-x-5 gap-y-2 justify-center">
        {labels.map((l) => (
          <div key={l.labelId} className="flex items-center gap-1.5">
            <span className="inline-block h-3 w-3 rounded-sm" style={{ backgroundColor: l.color }} />
            <span className="text-xs text-gray-700">{l.name}</span>
          </div>
        ))}
        <div className="flex items-center gap-1.5">
          <span className="inline-block h-3 w-3 rounded-sm" style={{ backgroundColor: UNLABELED_COLOR }} />
          <span className="text-xs italic text-gray-500">{UNLABELED_NAME}</span>
        </div>
      </div>
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────────
// Tabla cruzada períodos x etiquetas
// ────────────────────────────────────────────────────────────────────────────────
function PeriodsTable({
  periods, labels, totalAll, effectivenessAll,
}: {
  periods: ReportPeriod[]
  labels: { labelId: string; name: string; color: string; isEffective: boolean }[]
  totalAll: number
  effectivenessAll: number
}) {
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full border-collapse text-xs">
        <thead>
          <tr className="border-b-2 border-blue-200">
            <th className="text-left py-1.5 px-2 text-blue-800 font-semibold text-xs whitespace-nowrap">Clasificación</th>
            {periods.map((p) => (
              <th key={p.label} colSpan={2} className="text-center py-1.5 px-2 text-blue-800 font-semibold text-xs whitespace-nowrap border-l border-gray-200">
                {p.label}
              </th>
            ))}
            <th colSpan={2} className="text-center py-1.5 px-2 text-blue-800 font-semibold text-xs whitespace-nowrap border-l border-gray-200">
              Total
            </th>
          </tr>
          <tr className="border-b border-gray-200 text-[10px] uppercase tracking-wide text-gray-500">
            <th></th>
            {periods.map((p) => (
              <>
                <th key={p.label + '-c'} className="text-right py-0.5 px-2 font-medium border-l border-gray-100">Cant.</th>
                <th key={p.label + '-p'} className="text-right py-0.5 px-2 font-medium">%</th>
              </>
            ))}
            <th className="text-right py-0.5 px-2 font-medium border-l border-gray-100">Cant.</th>
            <th className="text-right py-0.5 px-2 font-medium">%</th>
          </tr>
        </thead>
        <tbody>
          {labels.map((l) => {
            const totalLabel = periods.reduce((s, p) => {
              const b = p.breakdown.find((x) => x.labelId === l.labelId)
              return s + (b?.count ?? 0)
            }, 0)
            const totalPct = totalAll === 0 ? 0 : Math.round((totalLabel / totalAll) * 100 * 10) / 10
            return (
              <tr key={l.labelId} className="border-b border-gray-100">
                <td className="py-1 px-2 text-gray-800 whitespace-nowrap">
                  <span className="inline-block h-2 w-2 rounded-sm mr-1.5 align-middle" style={{ backgroundColor: l.color }} />
                  {l.name}
                  {l.isEffective && (
                    <span className="ml-1.5 text-[9px] text-emerald-600 font-medium">✓ efectiva</span>
                  )}
                </td>
                {periods.map((p) => {
                  const b = p.breakdown.find((x) => x.labelId === l.labelId)
                  return (
                    <>
                      <td key={p.label + '-c'} className="text-right py-1 px-2 text-gray-800 border-l border-gray-100 whitespace-nowrap">
                        {b?.count ?? 0}
                      </td>
                      <td key={p.label + '-p'} className="text-right py-1 px-2 text-gray-500 whitespace-nowrap" style={l.isEffective ? { color: l.color, fontWeight: 600 } : {}}>
                        {b ? `${b.percentage}%` : '0%'}
                      </td>
                    </>
                  )
                })}
                <td className="text-right py-1 px-2 text-gray-900 font-semibold border-l border-gray-100 whitespace-nowrap">{totalLabel}</td>
                <td className="text-right py-1 px-2 text-gray-700 whitespace-nowrap">{totalPct}%</td>
              </tr>
            )
          })}

          {/* Sin etiqueta */}
          {periods.some((p) => p.unlabeledCount > 0) && (() => {
            const totalUn = periods.reduce((s, p) => s + p.unlabeledCount, 0)
            const totalUnPct = totalAll === 0 ? 0 : Math.round((totalUn / totalAll) * 100 * 10) / 10
            return (
              <tr className="border-b border-gray-100">
                <td className="py-1 px-2 italic text-gray-500 whitespace-nowrap">
                  <span className="inline-block h-2 w-2 rounded-sm mr-1.5 align-middle" style={{ backgroundColor: UNLABELED_COLOR }} />
                  {UNLABELED_NAME}
                </td>
                {periods.map((p) => (
                  <>
                    <td key={p.label + '-uc'} className="text-right py-1 px-2 text-gray-500 border-l border-gray-100 whitespace-nowrap">
                      {p.unlabeledCount}
                    </td>
                    <td key={p.label + '-up'} className="text-right py-1 px-2 text-gray-400 whitespace-nowrap">
                      {p.unlabeledPercentage}%
                    </td>
                  </>
                ))}
                <td className="text-right py-1 px-2 text-gray-700 font-semibold border-l border-gray-100 whitespace-nowrap">{totalUn}</td>
                <td className="text-right py-1 px-2 text-gray-500 whitespace-nowrap">{totalUnPct}%</td>
              </tr>
            )
          })()}

          <tr className="bg-blue-50 font-bold">
            <td className="py-1.5 px-2 text-blue-900 whitespace-nowrap">Grand Total</td>
            {periods.map((p) => (
              <>
                <td key={p.label + '-gt'} colSpan={2} className="text-center py-1.5 px-2 text-blue-900 border-l border-blue-100 whitespace-nowrap">
                  {p.total} ({p.effectiveness}% efec.)
                </td>
              </>
            ))}
            <td colSpan={2} className="text-center py-1.5 px-2 text-blue-900 border-l border-blue-100 whitespace-nowrap">
              {totalAll} ({effectivenessAll}% efec.)
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────────
// Helpers
// ────────────────────────────────────────────────────────────────────────────────
function formatDate(iso: string): string {
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

function isDark(hex: string): boolean {
  if (!hex.startsWith('#') || hex.length < 7) return false
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
  return luminance < 0.55
}

function keyFindings(
  data: ManagementReport,
  allLabels: { labelId: string; name: string; color: string; isEffective: boolean }[],
): string[] {
  if (data.periods.length === 0) return ['No hay datos en el período seleccionado.']

  const findings: string[] = []

  // 1) Volumen total
  findings.push(
    `Se gestionaron ${data.totalAll} conversaciones en el rango completo, ` +
    `con una efectividad agregada del ${data.effectivenessAll}%.`
  )

  // 2) Etiqueta dominante en el último período
  const last = data.periods[data.periods.length - 1]
  if (last.breakdown.length > 0) {
    const top = last.breakdown[0]
    findings.push(
      `En "${last.label}" la clasificación de mayor volumen fue "${top.name}" ` +
      `con ${top.count} casos.`
    )
  }

  // 3) Tendencia de efectividad
  if (data.periods.length >= 2) {
    const first = data.periods[0]
    const diff = last.effectiveness - first.effectiveness
    if (Math.abs(diff) >= 1) {
      const verb = diff > 0 ? 'aumentó' : 'disminuyó'
      findings.push(
        `La efectividad ${verb} ${Math.abs(diff).toFixed(1)} puntos porcentuales entre ` +
        `"${first.label}" y "${last.label}".`
      )
    }
  }

  // 4) Etiqueta efectiva con mejor desempeño global
  const effectiveLabels = allLabels.filter((l) => l.isEffective)
  if (effectiveLabels.length > 0) {
    const totalsByLabel = effectiveLabels.map((l) => ({
      name: l.name,
      total: data.periods.reduce((s, p) => {
        const b = p.breakdown.find((x) => x.labelId === l.labelId)
        return s + (b?.count ?? 0)
      }, 0),
    })).sort((a, b) => b.total - a.total)
    const topEffective = totalsByLabel[0]
    if (topEffective && topEffective.total > 0) {
      findings.push(
        `La etiqueta efectiva con mayor volumen fue "${topEffective.name}" ` +
        `con ${topEffective.total} casos.`
      )
    }
  }

  // 5) Sin respuesta (si existe en alguna forma)
  const sinResp = allLabels.find((l) =>
    /sin\s*respuesta|no\s*respondi[oó]/i.test(l.name)
  )
  if (sinResp) {
    const totalUn = data.periods.reduce((s, p) => {
      const b = p.breakdown.find((x) => x.labelId === sinResp.labelId)
      return s + (b?.count ?? 0)
    }, 0)
    if (totalUn > 0) {
      const pct = data.totalAll === 0 ? 0 : Math.round((totalUn / data.totalAll) * 100)
      findings.push(
        `La categoría "${sinResp.name}" representó el ${pct}% del total.`
      )
    }
  }

  return findings
}

