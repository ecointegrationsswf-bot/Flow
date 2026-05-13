import { useState } from 'react'
import { Download, ChevronDown, Loader2, FileDown } from 'lucide-react'
import { MorosidadHistoryTab } from './MorosidadHistoryTab'
import { useTenantActions, type TenantAction } from '@/modules/actions/hooks/useTenantActions'
import { getActionFriendlyName } from '@/shared/actionLabels'
import { useToast, ToastContainer } from '@/shared/components/Toast'

// Solo mostramos acciones marcadas explícitamente como descarga de morosidad.
const isDownloadAction = (action: TenantAction) => action.isDelinquencyDownload

// ─── Selector de acción ───────────────────────────────────────────────────────

function ActionSelector({
  actions,
  selected,
  onSelect,
}: {
  actions: TenantAction[]
  selected: string | null
  onSelect: (id: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')

  const friendlyLabel = (a: TenantAction) => getActionFriendlyName(a.name)

  const filtered = actions.filter((a) =>
    friendlyLabel(a).toLowerCase().includes(search.toLowerCase()) ||
    (a.description ?? '').toLowerCase().includes(search.toLowerCase()),
  )

  const selectedAction = actions.find((a) => a.id === selected)
  const selectedLabel = selectedAction ? friendlyLabel(selectedAction) : 'Seleccionar fuente de descarga...'

  return (
    <div className="relative">
      <button
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center justify-between rounded-lg border border-gray-300 bg-white px-4 py-2.5 text-sm shadow-sm hover:bg-gray-50 focus:outline-none"
      >
        <span className={selected ? 'font-medium text-gray-900' : 'text-gray-400'}>
          {selectedLabel}
        </span>
        <ChevronDown className={`h-4 w-4 text-gray-400 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <div className="absolute z-20 mt-1 w-full rounded-lg border border-gray-200 bg-white py-1 shadow-lg">
          {actions.length > 3 && (
            <div className="px-3 pb-1 pt-2">
              <input
                autoFocus
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Buscar..."
                className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
              />
            </div>
          )}
          <div className="max-h-48 overflow-y-auto">
            {filtered.length === 0 ? (
              <p className="px-4 py-3 text-sm text-gray-400">Sin resultados</p>
            ) : (
              filtered.map((action) => (
                <button
                  key={action.id}
                  onClick={() => { onSelect(action.id); setOpen(false); setSearch('') }}
                  className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm hover:bg-blue-50 ${
                    selected === action.id ? 'bg-blue-50 font-medium text-blue-700' : 'text-gray-700'
                  }`}
                >
                  <Download className="h-3.5 w-3.5 shrink-0 text-blue-400" />
                  <div>
                    <p>{friendlyLabel(action)}</p>
                    {action.description && (
                      <p className="text-xs text-gray-400">{action.description}</p>
                    )}
                  </div>
                </button>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  )
}

// ─── Botón de exportar Excel (tenant) ────────────────────────────────────────

interface ExportExcelButtonProps {
  executionId: string
  onError: (msg: string) => void
  onSuccess: (msg: string) => void
}

function ExportExcelButton({ executionId, onError, onSuccess }: ExportExcelButtonProps) {
  const [loading, setLoading] = useState(false)

  const handleExport = async () => {
    setLoading(true)
    try {
      const token = localStorage.getItem('token') ?? localStorage.getItem('sa_token') ?? ''
      const base  = (import.meta.env.VITE_API_BASE_URL ?? '/api') as string
      const url   = `${base}/morosidad/executions/${executionId}/export`
      const resp  = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
      if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
      const blob = await resp.blob()
      const href = URL.createObjectURL(blob)
      const a    = document.createElement('a')
      a.href = href
      const cd = resp.headers.get('content-disposition')
      a.download = cd?.match(/filename="?([^"]+)"?/)?.[1] ?? `descarga_${executionId.slice(0, 8)}.xlsx`
      document.body.appendChild(a); a.click(); document.body.removeChild(a)
      URL.revokeObjectURL(href)
      onSuccess('Archivo descargado correctamente.')
    } catch (e) {
      console.error('[Export]', e)
      onError('Error al exportar. Intenta de nuevo.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <button
      onClick={handleExport}
      disabled={loading}
      className="flex items-center gap-1.5 rounded-md border border-green-300 bg-green-50 px-3 py-1.5 text-xs font-medium text-green-700 hover:bg-green-100 disabled:opacity-50"
    >
      {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <FileDown className="h-3.5 w-3.5" />}
      Exportar Excel
    </button>
  )
}

// ─── Página principal ─────────────────────────────────────────────────────────

export function MorosidadPage() {
  const { data: allActions = [], isLoading } = useTenantActions()
  const [selectedActionId, setSelectedActionId] = useState<string | null>(null)
  const { toasts, remove, toast } = useToast()

  // Solo acciones de descarga
  const downloadActions = allActions.filter(isDownloadAction)

  // Si solo hay una, la preseleccionamos automáticamente
  const effectiveActionId = selectedActionId
    ?? (downloadActions.length === 1 ? downloadActions[0].id : null)

  // Wrapper que inyecta los callbacks de toast al botón de exportar
  const ExportWithToast = ({ executionId }: { executionId: string }) => (
    <ExportExcelButton
      executionId={executionId}
      onError={(msg) => toast.error(msg)}
      onSuccess={(msg) => toast.success(msg)}
    />
  )

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold text-gray-900">
          <Download className="h-6 w-6 text-blue-600" />
          Descargas
        </h1>
        <p className="mt-1 text-sm text-gray-500">
          Historial de datos descargados automáticamente desde fuentes externas.
          Consulta el detalle de cada ejecución y los registros generados.
        </p>
      </div>

      {/* Selector — solo si hay más de una acción de descarga */}
      {isLoading ? (
        <div className="flex items-center gap-2 text-sm text-gray-400">
          <Loader2 className="h-4 w-4 animate-spin" /> Cargando...
        </div>
      ) : downloadActions.length === 0 ? (
        <div className="rounded-xl border border-dashed border-gray-300 bg-white p-10 text-center">
          <Download className="mx-auto h-10 w-10 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">No tienes fuentes de descarga configuradas.</p>
          <p className="mt-1 text-xs text-gray-400">Contacta al administrador para configurar una descarga automática.</p>
        </div>
      ) : (
        <>
          {downloadActions.length > 1 && (
            <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
              <label className="mb-2 block text-sm font-semibold text-gray-700">
                Fuente de descarga
              </label>
              <ActionSelector
                actions={downloadActions}
                selected={effectiveActionId}
                onSelect={setSelectedActionId}
              />
            </div>
          )}

          {effectiveActionId && (
            <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
              <MorosidadHistoryTab
                actionId={effectiveActionId}
                exportButton={ExportWithToast}
              />
            </div>
          )}
        </>
      )}

      {/* Toast notifications */}
      <ToastContainer toasts={toasts} onRemove={remove} />
    </div>
  )
}
