import { useState } from 'react'
import { Link } from 'react-router-dom'
import { FileSpreadsheet, Download, Loader2, Calendar, Filter, ArrowLeft, Info } from 'lucide-react'
import { useCampaignTemplates } from '@/shared/hooks/useCampaignTemplates'
import { useDownloadConversationDetailsExcel } from '@/modules/reports/hooks/useEffectivenessReport'
import { useToast, ToastContainer } from '@/shared/components/Toast'

// ── Helpers para fechas (ISO yyyy-MM-dd en hora local) ──────────────
function todayIso(addDays = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + addDays)
  return d.toISOString().slice(0, 10)
}

const QUICK_RANGES = [
  { label: 'Últimos 7 días',  days: 7  },
  { label: 'Últimos 30 días', days: 30 },
  { label: 'Últimos 90 días', days: 90 },
] as const

export function ConversationDetailsPage() {
  const [from, setFrom] = useState(todayIso(-30))
  const [to, setTo] = useState(todayIso(0))
  const [campaignTemplateId, setCampaignTemplateId] = useState<string>('')
  const [includeInbound, setIncludeInbound] = useState(false)

  const { data: templates = [] } = useCampaignTemplates()
  const download = useDownloadConversationDetailsExcel()
  const { toasts, remove, toast } = useToast()

  const handleQuickRange = (days: number) => {
    setFrom(todayIso(-days))
    setTo(todayIso(0))
  }

  const handleDownload = async () => {
    try {
      await download.mutateAsync({
        from, to,
        campaignTemplateId: campaignTemplateId || null,
        includeInboundWithoutCampaign: includeInbound,
      })
      toast.success('Excel descargado.')
    } catch (err: unknown) {
      const e = err as { message?: string }
      toast.error(e.message ?? 'No se pudo descargar el Excel.')
    }
  }

  return (
    <div className="px-6 py-4">
      <Link to="/reports" className="inline-flex items-center gap-1 text-sm text-blue-600 hover:underline mb-3">
        <ArrowLeft className="h-4 w-4" /> Volver a Informes
      </Link>

      <div className="mb-4">
        <h1 className="flex items-center gap-2 text-xl font-bold text-gray-900">
          <FileSpreadsheet className="h-5 w-5 text-emerald-600" />
          Detalle de Conversaciones
        </h1>
        <p className="text-xs text-gray-500 mt-1">
          Descarga el Excel con el detalle de gestión por conversación —
          mismo formato que el resumen automático que sale por correo cada mañana.
          A diferencia del correo, acá podés elegir el rango y el maestro a voluntad.
        </p>
      </div>

      {/* ── Filtros ───────────────────────────────────────────────── */}
      <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm max-w-3xl">
        <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-gray-700">
          <Filter className="h-4 w-4" />
          Filtros
        </div>

        {/* Rango de fechas */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 mb-3">
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">
              <Calendar className="inline h-3 w-3" /> Desde
            </label>
            <input
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              className="w-full rounded border border-gray-300 px-2 py-1.5 text-xs focus:border-blue-500 focus:outline-none"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">
              <Calendar className="inline h-3 w-3" /> Hasta
            </label>
            <input
              type="date"
              value={to}
              onChange={(e) => setTo(e.target.value)}
              className="w-full rounded border border-gray-300 px-2 py-1.5 text-xs focus:border-blue-500 focus:outline-none"
            />
          </div>
          <div className="flex items-end gap-1 flex-wrap">
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

        {/* Maestro (opcional) */}
        <div className="mb-3">
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Maestro de campaña <span className="text-gray-400 font-normal">(opcional — vacío = todos)</span>
          </label>
          <select
            value={campaignTemplateId}
            onChange={(e) => setCampaignTemplateId(e.target.value)}
            className="w-full rounded border border-gray-300 px-2 py-1.5 text-xs focus:border-blue-500 focus:outline-none"
          >
            <option value="">Todos los maestros</option>
            {templates.map(t => (
              <option key={t.id} value={t.id}>{t.name}</option>
            ))}
          </select>
        </div>

        {/* Toggle inbound sin campaña */}
        <div className="mb-4">
          <label className="flex items-start gap-2 cursor-pointer group">
            <div className="relative pt-0.5">
              <input
                type="checkbox"
                checked={includeInbound}
                onChange={(e) => setIncludeInbound(e.target.checked)}
                className="peer sr-only"
              />
              <div className="block h-5 w-9 rounded-full bg-gray-300 transition-colors peer-checked:bg-emerald-500" />
              <div className="absolute left-0.5 top-1 h-4 w-4 rounded-full bg-white shadow transition-transform peer-checked:translate-x-4" />
            </div>
            <div className="flex-1">
              <p className="text-xs font-medium text-gray-800">
                Incluir conversaciones inbound sin campaña
              </p>
              <p className="text-[11px] text-gray-500 mt-0.5">
                Agrega al Excel las conversaciones donde el cliente nos escribió
                espontáneamente en el rango (sin que nosotros le hayamos mandado
                un mensaje desde una campaña). Aparecen con "(sin campaña)" en la columna Campaña.
              </p>
            </div>
          </label>
        </div>

        {/* Info box */}
        <div className="rounded-md bg-blue-50 border border-blue-200 p-2.5 mb-4 flex items-start gap-2">
          <Info className="h-4 w-4 text-blue-600 mt-0.5 shrink-0" />
          <p className="text-[11px] text-blue-900 leading-relaxed">
            El Excel trae las columnas: <strong>Campaña, Cliente, Celular, Identificación,
            Fecha Gestión, Etiqueta, Resumen, Usuario, Apellido, Agente</strong>.
            Mismo formato que el correo automático del job nocturno.
          </p>
        </div>

        {/* Botón descargar */}
        <button
          onClick={handleDownload}
          disabled={download.isPending}
          className="inline-flex items-center gap-2 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
        >
          {download.isPending
            ? <><Loader2 className="h-4 w-4 animate-spin" /> Generando Excel...</>
            : <><Download className="h-4 w-4" /> Descargar Excel</>}
        </button>
      </div>

      <ToastContainer toasts={toasts} onRemove={remove} />
    </div>
  )
}
