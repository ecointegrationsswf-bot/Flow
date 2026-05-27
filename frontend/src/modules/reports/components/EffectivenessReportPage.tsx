import { useState, useMemo } from 'react'
import { Link } from 'react-router-dom'
import { FileBarChart, Download, Loader2, Calendar, Filter, FileText, ArrowLeft } from 'lucide-react'
import {
  useCampaignTemplatesForFilter,
  useGenerateEffectivenessReport,
  useDownloadEffectivenessPdf,
  type EffectivenessReport,
  type ResultDistributionBucket,
  type ContactDistributionBucket,
  type CampaignBreakdown,
} from '@/modules/reports/hooks/useEffectivenessReport'

// ── Rangos rápidos predefinidos ──────────────────────────────────────
function todayIso(addDays = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + addDays)
  return d.toISOString().slice(0, 10)
}

const QUICK_RANGES = [
  { label: 'Últimos 7 días',  days: 7 },
  { label: 'Últimos 30 días', days: 30 },
  { label: 'Últimos 90 días', days: 90 },
] as const

// ── Categorías para colorear ────────────────────────────────────────
function categoryClass(cat: ResultDistributionBucket['category']): string {
  switch (cat) {
    case 'Conversión':    return 'text-green-700 bg-green-50 border-green-200'
    case 'Compromiso':    return 'text-green-700 bg-green-50 border-green-200'
    case 'Negociación':   return 'text-gray-800 bg-gray-50 border-gray-200'
    case 'Disputa':       return 'text-amber-700 bg-amber-50 border-amber-200'
    case 'Cancelación':   return 'text-red-700 bg-red-50 border-red-200'
    case 'Sin Respuesta': return 'text-gray-500 bg-gray-50 border-gray-200'
  }
}

export function EffectivenessReportPage() {
  const [from, setFrom] = useState(todayIso(-30))
  const [to, setTo] = useState(todayIso(0))
  const [selectedTemplateIds, setSelectedTemplateIds] = useState<string[]>([])
  const [report, setReport] = useState<EffectivenessReport | null>(null)

  const { data: templates = [], isLoading: loadingTemplates } = useCampaignTemplatesForFilter(from, to)
  const generate = useGenerateEffectivenessReport()
  const downloadPdf = useDownloadEffectivenessPdf()

  const filters = useMemo(() => ({
    from, to,
    campaignTemplateIds: selectedTemplateIds.length > 0 ? selectedTemplateIds : undefined,
  }), [from, to, selectedTemplateIds])

  const handleQuickRange = (days: number) => {
    setFrom(todayIso(-days))
    setTo(todayIso(0))
    setReport(null)
  }

  const handleGenerate = () => {
    generate.mutate(filters, {
      onSuccess: (data) => setReport(data),
    })
  }

  const handleDownloadPdf = () => {
    downloadPdf.mutate(filters)
  }

  const toggleTemplate = (id: string) => {
    setSelectedTemplateIds(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id])
  }

  return (
    <div className="px-6 py-4">
      <Link to="/reports" className="inline-flex items-center gap-1 text-sm text-blue-600 hover:underline mb-3">
        <ArrowLeft className="h-4 w-4" /> Volver a Informes
      </Link>
      <div className="mb-4 flex items-start justify-between">
        <div>
          <h1 className="flex items-center gap-2 text-xl font-bold text-gray-900">
            <FileBarChart className="h-5 w-5 text-blue-600" />
            Informe de Efectividad
          </h1>
          <p className="text-xs text-gray-500">
            Reporte de efectividad por <strong>cliente único</strong> (no por mensaje-evento).
            Filtrá rango y maestros de campaña, generá vista previa, descargá PDF nativo.
          </p>
        </div>
      </div>

      {/* ── Filtros ──────────────────────────────── */}
      <div className="mb-4 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-gray-700">
          <Filter className="h-4 w-4" />
          Filtros
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 mb-3">
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">
              <Calendar className="inline h-3 w-3" /> Desde
            </label>
            <input
              type="date"
              value={from}
              onChange={(e) => { setFrom(e.target.value); setReport(null) }}
              className="w-full rounded border border-gray-300 px-2 py-1.5 text-xs"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">
              <Calendar className="inline h-3 w-3" /> Hasta
            </label>
            <input
              type="date"
              value={to}
              onChange={(e) => { setTo(e.target.value); setReport(null) }}
              className="w-full rounded border border-gray-300 px-2 py-1.5 text-xs"
            />
          </div>
          <div className="flex items-end gap-1">
            {QUICK_RANGES.map(r => (
              <button
                key={r.days}
                onClick={() => handleQuickRange(r.days)}
                className="rounded border border-gray-300 px-2 py-1.5 text-[11px] font-medium text-gray-700 hover:bg-gray-50"
              >
                {r.label}
              </button>
            ))}
          </div>
        </div>

        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Maestros de campaña (opcional — vacío = todos los del rango)
          </label>
          {loadingTemplates ? (
            <div className="flex items-center gap-2 text-xs text-gray-500">
              <Loader2 className="h-3 w-3 animate-spin" /> Cargando maestros...
            </div>
          ) : templates.length === 0 ? (
            <div className="text-xs text-gray-500">No hay maestros de campaña con corridas en este rango.</div>
          ) : (
            <div className="flex flex-wrap gap-1.5 max-h-32 overflow-y-auto rounded border border-gray-200 bg-gray-50 p-2">
              {templates.map(t => {
                const selected = selectedTemplateIds.includes(t.id)
                return (
                  <button
                    key={t.id}
                    onClick={() => toggleTemplate(t.id)}
                    className={`rounded-full border px-2 py-0.5 text-[11px] font-medium transition-colors ${
                      selected
                        ? 'bg-blue-100 border-blue-300 text-blue-700'
                        : 'bg-white border-gray-300 text-gray-700 hover:bg-gray-50'
                    }`}
                    title={`${t.campaignCount} corrida(s) · ${t.totalContacts} contactos · última ${new Date(t.lastCreatedAt).toLocaleDateString('es-PA')}`}
                  >
                    {selected && '✓ '}{t.name}
                    <span className="ml-1 text-[10px] text-gray-500">({t.campaignCount})</span>
                  </button>
                )
              })}
            </div>
          )}
          {selectedTemplateIds.length > 0 && (
            <div className="mt-1 flex items-center justify-between text-[11px] text-gray-500">
              <span>{selectedTemplateIds.length} maestro(s) seleccionado(s)</span>
              <button
                onClick={() => setSelectedTemplateIds([])}
                className="text-blue-600 hover:underline"
              >
                Limpiar selección
              </button>
            </div>
          )}
        </div>

        <div className="mt-4 flex items-center gap-2">
          <button
            onClick={handleGenerate}
            disabled={generate.isPending}
            className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-4 py-2 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {generate.isPending
              ? <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Generando...</>
              : <><FileBarChart className="h-3.5 w-3.5" /> Generar vista previa</>}
          </button>
          <button
            onClick={handleDownloadPdf}
            disabled={downloadPdf.isPending}
            className="flex items-center gap-1.5 rounded-lg border border-gray-300 bg-white px-4 py-2 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            {downloadPdf.isPending
              ? <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Descargando...</>
              : <><Download className="h-3.5 w-3.5" /> Descargar PDF</>}
          </button>
        </div>
      </div>

      {/* ── Vista previa del reporte ───────────────────── */}
      {!report ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-12 text-center">
          <FileText className="mx-auto h-12 w-12 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">
            Configurá los filtros y presioná <strong>Generar vista previa</strong> para ver las métricas.
          </p>
        </div>
      ) : (
        <ReportPreview report={report} />
      )}
    </div>
  )
}

// ── Subcomponente: vista previa del reporte ───────────────────────
function ReportPreview({ report }: { report: EffectivenessReport }) {
  const s = report.summary

  return (
    <div className="space-y-4">
      {/* KPIs */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
        <Kpi label="Clientes únicos contactados" value={s.uniqueClients.toString()} subtitle={`${s.totalContactsSent} mensajes enviados`} color="text-blue-700" />
        <Kpi label="Promedio contactos / cliente" value={s.averageContactsPerClient.toFixed(2)} subtitle="Saturación del contacto" color="text-amber-700" />
        <Kpi label="Engagement" value={`${s.engagementRate.toFixed(1)}%`} subtitle={`${s.clientsWhoResponded} clientes respondieron`} color="text-green-700" />
        <Kpi label="Gestión efectiva" value={`${s.effectiveManagementRate.toFixed(1)}%`} subtitle={`${s.clientsConfirmedPayment + s.clientsWithPromise + s.clientsNegotiating} de ${s.uniqueClients} clientes`} color="text-green-700" />
      </div>

      {/* Distribución de contactos */}
      <Card title="Distribución de contactos por cliente">
        <table className="w-full text-xs">
          <thead className="bg-gray-50 text-left text-gray-600">
            <tr>
              <th className="px-3 py-2">Veces contactado</th>
              <th className="px-3 py-2 text-center">Clientes</th>
              <th className="px-3 py-2 text-center">% del universo</th>
              <th className="px-3 py-2 text-center">Mensajes enviados</th>
            </tr>
          </thead>
          <tbody>
            {report.contactDistribution.buckets.map((b: ContactDistributionBucket) => (
              <tr key={b.contactCount} className="border-t border-gray-100">
                <td className="px-3 py-2">{b.contactCount >= 4 ? '4+ veces' : `${b.contactCount} vez${b.contactCount === 1 ? '' : 'es'}`}</td>
                <td className="px-3 py-2 text-center font-mono">{b.clients}</td>
                <td className="px-3 py-2 text-center">{b.percentageOfUniqueClients.toFixed(1)}%</td>
                <td className="px-3 py-2 text-center">{b.clients * b.contactCount}</td>
              </tr>
            ))}
            <tr className="border-t-2 border-blue-200 bg-blue-50 font-semibold">
              <td className="px-3 py-2">TOTAL</td>
              <td className="px-3 py-2 text-center font-mono">{s.uniqueClients}</td>
              <td className="px-3 py-2 text-center">100.0%</td>
              <td className="px-3 py-2 text-center">{s.totalContactsSent}</td>
            </tr>
          </tbody>
        </table>
      </Card>

      {/* Distribución de resultados */}
      <Card title="Resultado por cliente único">
        <table className="w-full text-xs">
          <thead className="bg-gray-50 text-left text-gray-600">
            <tr>
              <th className="px-3 py-2">Resultado final</th>
              <th className="px-3 py-2 text-center">Clientes</th>
              <th className="px-3 py-2 text-center">% del total</th>
              <th className="px-3 py-2">Categoría</th>
            </tr>
          </thead>
          <tbody>
            {report.resultDistribution.buckets.map((b: ResultDistributionBucket) => (
              <tr key={b.label} className="border-t border-gray-100">
                <td className="px-3 py-2">{b.label}</td>
                <td className="px-3 py-2 text-center font-mono font-semibold">{b.clients}</td>
                <td className="px-3 py-2 text-center font-mono">{b.percentage.toFixed(1)}%</td>
                <td className="px-3 py-2">
                  <span className={`rounded-full border px-2 py-0.5 text-[10px] font-medium ${categoryClass(b.category)}`}>
                    {b.category}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {/* Breakdown por campaña */}
      {report.campaigns.length > 0 && (
        <Card title={`Detalle por campaña (${report.campaigns.length})`}>
          <table className="w-full text-xs">
            <thead className="bg-gray-50 text-left text-gray-600">
              <tr>
                <th className="px-3 py-2">Campaña</th>
                <th className="px-3 py-2 text-center">Lanzada</th>
                <th className="px-3 py-2 text-center">Contactos</th>
                <th className="px-3 py-2 text-center">Únicos</th>
                <th className="px-3 py-2 text-center">Respondieron</th>
                <th className="px-3 py-2 text-center">Pagaron</th>
              </tr>
            </thead>
            <tbody>
              {report.campaigns.map((c: CampaignBreakdown) => (
                <tr key={c.campaignId} className="border-t border-gray-100">
                  <td className="px-3 py-2">{c.campaignName}</td>
                  <td className="px-3 py-2 text-center font-mono text-gray-500">{new Date(c.launchedAtUtc).toLocaleDateString('es-PA')}</td>
                  <td className="px-3 py-2 text-center font-mono">{c.totalContacts}</td>
                  <td className="px-3 py-2 text-center font-mono">{c.uniqueClients}</td>
                  <td className="px-3 py-2 text-center font-mono">{c.responded}</td>
                  <td className="px-3 py-2 text-center font-mono text-green-700 font-semibold">{c.confirmedPayment}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}

      <div className="text-[11px] text-gray-500 italic px-1">
        Generado el {new Date(report.filters.generatedAtUtc).toLocaleString('es-PA')} · Tenant: {report.filters.tenantName}
      </div>
    </div>
  )
}

function Kpi({ label, value, subtitle, color }: { label: string; value: string; subtitle: string; color: string }) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <div className="text-[10px] uppercase tracking-wide text-gray-500">{label}</div>
      <div className={`mt-1 text-3xl font-bold ${color}`}>{value}</div>
      <div className="mt-1 text-[11px] text-gray-700">{subtitle}</div>
    </div>
  )
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <div className="border-b border-gray-200 px-4 py-2 text-sm font-semibold text-gray-800">{title}</div>
      <div className="overflow-x-auto">{children}</div>
    </div>
  )
}
