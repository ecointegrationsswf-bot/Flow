import { useState, useMemo } from 'react'
import { Tag, MessageSquare, Megaphone, Loader2 } from 'lucide-react'
import { useLabelingSummary, type LabelingSummary } from '@/shared/hooks/useDashboard'

const UNLABELED_COLOR = '#cbd5e1' // slate-300

const MONTH_NAMES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
]

// Lista de años: 2 atrás y 1 adelante del actual (Panamá)
function getYearOptions(): number[] {
  const now = new Date()
  const panama = new Date(now.getTime() - 5 * 60 * 60 * 1000)
  const y = panama.getUTCFullYear()
  return [y - 2, y - 1, y, y + 1]
}

function getDefaultPeriod(): { year: number; month: number } {
  const now = new Date()
  const panama = new Date(now.getTime() - 5 * 60 * 60 * 1000)
  return { year: panama.getUTCFullYear(), month: panama.getUTCMonth() + 1 }
}

// ── DonutChart SVG manual — sin dependencias externas ──────────────────────────
function DonutChart({ data, size = 220 }: { data: { value: number; color: string; label: string }[]; size?: number }) {
  const total = data.reduce((s, d) => s + d.value, 0)
  const cx = size / 2
  const cy = size / 2
  const r = size / 2 - 4
  const innerR = r * 0.55
  const cir = 2 * Math.PI * r

  if (total === 0) {
    return (
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
        <circle cx={cx} cy={cy} r={r} fill="none" stroke="#e2e8f0" strokeWidth={r - innerR} />
        <text x={cx} y={cy} textAnchor="middle" dominantBaseline="middle" fill="#94a3b8" fontSize="14">
          Sin datos
        </text>
      </svg>
    )
  }

  let accumulated = 0
  const arcs = data.map((d, i) => {
    const fraction = d.value / total
    const len = fraction * cir
    const offset = -accumulated // negativo: empezar desde top
    accumulated += len
    return { ...d, len, offset, i }
  })

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
      <g transform={`rotate(-90 ${cx} ${cy})`}>
        {/* base track */}
        <circle cx={cx} cy={cy} r={r} fill="none" stroke="#f1f5f9" strokeWidth={r - innerR} />
        {/* arcos */}
        {arcs.map((a) => (
          <circle
            key={a.i}
            cx={cx}
            cy={cy}
            r={r}
            fill="none"
            stroke={a.color}
            strokeWidth={r - innerR}
            strokeDasharray={`${a.len} ${cir - a.len}`}
            strokeDashoffset={a.offset}
          />
        ))}
      </g>
      {/* total al centro */}
      <text x={cx} y={cy - 6} textAnchor="middle" fill="#0f172a" fontSize="22" fontWeight="700">
        {total}
      </text>
      <text x={cx} y={cy + 14} textAnchor="middle" fill="#64748b" fontSize="11">
        conversaciones
      </text>
    </svg>
  )
}

// ── KPI Card ──────────────────────────────────────────────────────────────────
function KpiCard({
  label, value, sub, bgClass, labelClass, valueClass, subClass, icon: Icon,
}: {
  label: string
  value: string | number
  sub: string
  bgClass: string
  labelClass: string
  valueClass: string
  subClass: string
  icon: React.ComponentType<{ className?: string }>
}) {
  return (
    <div className={`rounded-lg border px-4 py-3 ${bgClass}`}>
      <div className={`flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wide ${labelClass}`}>
        <Icon className="h-3 w-3" />
        {label}
      </div>
      <div className={`mt-1 text-2xl font-bold leading-none ${valueClass}`}>{value}</div>
      <div className={`mt-1 text-[11px] font-medium ${subClass}`}>{sub}</div>
    </div>
  )
}

// ── Componente principal ──────────────────────────────────────────────────────
export function LabelingDistributionCard() {
  const defaults = useMemo(getDefaultPeriod, [])
  const yearOptions = useMemo(getYearOptions, [])
  const [year, setYear] = useState(defaults.year)
  const [month, setMonth] = useState(defaults.month)

  const { data, isLoading, isError } = useLabelingSummary(year, month)

  const chartData = useMemo(() => {
    if (!data) return []
    const arr = data.breakdown.map((b) => ({
      label: b.name,
      value: b.count,
      color: b.color,
    }))
    if (data.unlabeledCount > 0) {
      arr.push({ label: 'Sin etiqueta', value: data.unlabeledCount, color: UNLABELED_COLOR })
    }
    return arr
  }, [data])

  return (
    <div className="rounded-lg bg-white p-5 shadow-sm">
      {/* Header con título y selectores */}
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Tag className="h-4 w-4 text-blue-600" />
          <h3 className="text-sm font-semibold text-gray-900">Distribución de etiquetas</h3>
        </div>
        <div className="flex items-center gap-2">
          <select
            value={month}
            onChange={(e) => setMonth(parseInt(e.target.value, 10))}
            className="rounded-md border border-gray-300 px-2 py-1 text-xs text-gray-700 focus:border-blue-500 focus:outline-none"
          >
            {MONTH_NAMES.map((m, i) => (
              <option key={i + 1} value={i + 1}>{m}</option>
            ))}
          </select>
          <select
            value={year}
            onChange={(e) => setYear(parseInt(e.target.value, 10))}
            className="rounded-md border border-gray-300 px-2 py-1 text-xs text-gray-700 focus:border-blue-500 focus:outline-none"
          >
            {yearOptions.map((y) => (
              <option key={y} value={y}>{y}</option>
            ))}
          </select>
        </div>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-12 text-gray-400">
          <Loader2 className="h-5 w-5 animate-spin mr-2" /> Cargando…
        </div>
      ) : isError ? (
        <div className="py-12 text-center text-sm text-red-500">
          Error al cargar el resumen.
        </div>
      ) : data ? (
        <>
          {/* KPI Cards */}
          <div className="grid grid-cols-3 gap-3 mb-4">
            <KpiCard
              label="Etiquetadas"
              value={data.taggedCount}
              sub={`${data.taggedPercentage}%`}
              bgClass="bg-blue-50 border-blue-200"
              labelClass="text-blue-700"
              valueClass="text-blue-900"
              subClass="text-blue-600"
              icon={Tag}
            />
            <KpiCard
              label="Total convs"
              value={data.totalConversations}
              sub="procesadas"
              bgClass="bg-slate-50 border-slate-200"
              labelClass="text-slate-600"
              valueClass="text-slate-900"
              subClass="text-slate-500"
              icon={MessageSquare}
            />
            <KpiCard
              label="Campañas"
              value={data.campaignsInReport}
              sub="en el reporte"
              bgClass="bg-amber-50 border-amber-200"
              labelClass="text-amber-700"
              valueClass="text-amber-900"
              subClass="text-amber-600"
              icon={Megaphone}
            />
          </div>

          {/* Chart + leyenda */}
          {data.totalConversations === 0 ? (
            <div className="rounded-lg border border-dashed border-gray-200 bg-gray-50 py-10 text-center text-sm text-gray-400">
              No hay conversaciones en el período seleccionado.
            </div>
          ) : (
            <div className="flex flex-wrap items-center gap-6 rounded-lg border border-gray-100 bg-gray-50/50 p-4">
              <DonutChart data={chartData} />
              <div className="flex-1 min-w-[200px]">
                <ul className="space-y-1.5">
                  {chartData.map((item) => (
                    <li key={item.label} className="flex items-center gap-2 text-sm">
                      <span
                        className="inline-block h-2.5 w-2.5 shrink-0 rounded-sm"
                        style={{ backgroundColor: item.color }}
                      />
                      <span className={`flex-1 truncate ${item.label === 'Sin etiqueta' ? 'italic text-gray-500' : 'text-gray-800'}`}>
                        {item.label}
                      </span>
                      <span className="font-semibold text-gray-900">{item.value}</span>
                      <span className="text-xs text-gray-400 w-12 text-right">
                        ({Math.round((item.value / data.totalConversations) * 100)}%)
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          )}
        </>
      ) : null}
    </div>
  )
}

// Re-export para que el dashboard pueda usar el tipo si lo necesita
export type { LabelingSummary }
