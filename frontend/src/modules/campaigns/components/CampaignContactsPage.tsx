import { useState, useMemo } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  ArrowLeft, Search, X, Loader2, FileDown, CheckCircle2,
  Clock, AlertTriangle, MinusCircle, AlertCircle, Eye, MessageSquare,
} from 'lucide-react'
import {
  useCampaignContacts, useCampaignById, useExportCampaignContacts,
  type ContactStatusFilter, type CampaignContactRow,
} from '@/shared/hooks/useCampaigns'
import { useToast, ToastContainer } from '@/shared/components/Toast'
import { useTenantTime } from '@/shared/hooks/useTenantTime'

const PAGE_SIZE = 50

// Mapeo de estados crudos del backend → bucket visual del filtro
const BUCKET_BY_STATUS: Record<string, ContactStatusFilter> = {
  Sent: 'Sent',
  Pending: 'Pending',
  Queued: 'Pending',
  Claimed: 'Pending',
  Deferred: 'Pending',
  Retry: 'Pending',
  Error: 'Failed',
  Skipped: 'Discarded',
  Duplicate: 'Discarded',
}

// Etiqueta humana de cada estado crudo
const STATUS_LABEL: Record<string, string> = {
  Sent: 'Enviado',
  Pending: 'En cola',
  Queued: 'En cola',
  Claimed: 'Enviando',
  Deferred: 'Programado',
  Retry: 'Reintentando',
  Error: 'Error',
  Skipped: 'Descartado',
  Duplicate: 'Duplicado',
}

function StatusBadge({ status, error }: { status: string; error: string | null }) {
  const bucket = BUCKET_BY_STATUS[status] ?? 'All'
  const label = STATUS_LABEL[status] ?? status
  const config: Record<ContactStatusFilter, { icon: React.ComponentType<{ className?: string }>; cls: string }> = {
    All:       { icon: MinusCircle, cls: 'bg-gray-100 text-gray-600 border-gray-200' },
    Sent:      { icon: CheckCircle2, cls: 'bg-emerald-50 text-emerald-700 border-emerald-200' },
    Pending:   { icon: Clock, cls: 'bg-amber-50 text-amber-700 border-amber-200' },
    Failed:    { icon: AlertCircle, cls: 'bg-red-50 text-red-700 border-red-200' },
    Discarded: { icon: MinusCircle, cls: 'bg-gray-100 text-gray-500 border-gray-200' },
  }
  const c = config[bucket]
  const Icon = c.icon
  return (
    <div className="inline-flex flex-col items-start">
      <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium ${c.cls}`}>
        <Icon className="h-3 w-3" />
        {label}
      </span>
      {bucket === 'Failed' && error && (
        <span
          className="mt-1 max-w-xs truncate text-[11px] text-red-500"
          title={error}
        >
          <AlertTriangle className="inline h-3 w-3 mr-0.5" />
          {error}
        </span>
      )}
    </div>
  )
}

export function CampaignContactsPage() {
  const { id = '' } = useParams<{ id: string }>()
  const tt = useTenantTime()
  const { toasts, remove, toast } = useToast()

  const [status, setStatus] = useState<ContactStatusFilter>('All')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [messageContact, setMessageContact] = useState<CampaignContactRow | null>(null)

  const { data: campaign, isLoading: loadingCampaign } = useCampaignById(id)
  const { data, isLoading, isFetching, isError } = useCampaignContacts(
    { campaignId: id, status, q: search, page, pageSize: PAGE_SIZE },
    !!id,
  )
  const exportMutation = useExportCampaignContacts()

  const counts = data?.counts
  const total = data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  // Reset de paginación cuando cambian filtros
  const handleStatus = (s: ContactStatusFilter) => {
    setStatus(s)
    setPage(1)
  }
  const handleSearch = (q: string) => {
    setSearch(q)
    setPage(1)
  }

  const handleExport = async () => {
    try {
      await exportMutation.mutateAsync({ campaignId: id, status, q: search })
      toast.success('Excel descargado.')
    } catch (err: unknown) {
      const e = err as { message?: string }
      toast.error(e.message ?? 'No se pudo exportar.')
    }
  }

  const tabs: { key: ContactStatusFilter; label: string; count?: number }[] = useMemo(() => [
    { key: 'All',       label: 'Todos',       count: counts?.all },
    { key: 'Sent',      label: 'Enviados',    count: counts?.sent },
    { key: 'Pending',   label: 'Pendientes',  count: counts?.pending },
    { key: 'Failed',    label: 'Errores',     count: counts?.failed },
    { key: 'Discarded', label: 'Descartados', count: counts?.discarded },
  ], [counts])

  return (
    <div className="p-6">
      {/* Header */}
      <div className="mb-6">
        <Link
          to="/campaigns"
          className="inline-flex items-center gap-1 text-sm text-blue-600 hover:underline mb-3"
        >
          <ArrowLeft className="h-4 w-4" /> Volver a Campañas
        </Link>
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold text-gray-900">
              {loadingCampaign ? 'Cargando…' : campaign?.name ?? 'Campaña'}
            </h1>
            {campaign && (
              <p className="text-sm text-gray-500 mt-1">
                {campaign.processedContacts ?? 0} / {campaign.totalContacts ?? 0} procesados
                {' · '}
                {campaign.channel}
              </p>
            )}
          </div>
          <button
            onClick={handleExport}
            disabled={exportMutation.isPending || total === 0}
            className="inline-flex items-center gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-700 hover:bg-emerald-100 disabled:opacity-50"
          >
            {exportMutation.isPending
              ? <Loader2 className="h-4 w-4 animate-spin" />
              : <FileDown className="h-4 w-4" />}
            Exportar Excel
          </button>
        </div>
      </div>

      {/* Tabs filtro */}
      <div className="mb-3 flex flex-wrap gap-2">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => handleStatus(t.key)}
            className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-medium transition-colors ${
              status === t.key
                ? 'border-blue-500 bg-blue-50 text-blue-700'
                : 'border-gray-200 bg-white text-gray-600 hover:bg-gray-50'
            }`}
          >
            {t.label}
            {typeof t.count === 'number' && (
              <span className={`rounded-full px-1.5 py-0.5 text-[10px] ${
                status === t.key ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-500'
              }`}>
                {t.count}
              </span>
            )}
          </button>
        ))}
      </div>

      {/* Buscador */}
      <div className="mb-4 flex items-center gap-2">
        <div className="relative flex-1 max-w-md">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
          <input
            type="text"
            value={search}
            onChange={(e) => handleSearch(e.target.value)}
            placeholder="Buscar por nombre o teléfono…"
            className="w-full rounded-lg border border-gray-300 bg-white py-2 pl-9 pr-9 text-sm focus:border-blue-500 focus:outline-none"
          />
          {search && (
            <button
              onClick={() => handleSearch('')}
              className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
              title="Limpiar"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
        {isFetching && !isLoading && (
          <Loader2 className="h-4 w-4 animate-spin text-gray-400" />
        )}
      </div>

      {/* Tabla */}
      <div className="overflow-hidden rounded-lg bg-white shadow-sm">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Cliente</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Teléfono</th>
              <th className="px-4 py-3 text-center text-xs font-medium uppercase text-gray-500">Comentario</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Estado</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Enviado</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {isLoading ? (
              <tr><td colSpan={5} className="px-4 py-10 text-center text-sm text-gray-400">
                <Loader2 className="inline h-4 w-4 animate-spin mr-2" /> Cargando…
              </td></tr>
            ) : isError ? (
              <tr><td colSpan={5} className="px-4 py-10 text-center text-sm text-red-500">
                Error al cargar los contactos.
              </td></tr>
            ) : (data?.items ?? []).length === 0 ? (
              <tr><td colSpan={5} className="px-4 py-10 text-center text-sm text-gray-400">
                No hay contactos con los filtros actuales.
              </td></tr>
            ) : (
              data!.items.map((cc) => (
                <tr key={cc.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3">
                    <p className="text-sm text-gray-900">{cc.clientName ?? '—'}</p>
                  </td>
                  <td className="px-4 py-3">
                    <span className="font-mono text-xs text-gray-600">{cc.phoneNumber}</span>
                    {!cc.isPhoneValid && (
                      <span className="ml-2 text-[10px] text-amber-600">(inválido)</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {cc.generatedMessage ? (
                      <button
                        onClick={() => setMessageContact(cc)}
                        title="Ver mensaje enviado"
                        className="inline-flex items-center justify-center rounded-md p-1.5 text-gray-500 hover:bg-blue-50 hover:text-blue-600 transition-colors"
                      >
                        <Eye className="h-4 w-4" />
                      </button>
                    ) : (
                      <span className="text-gray-300">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <StatusBadge status={cc.dispatchStatus} error={cc.dispatchError} />
                  </td>
                  <td className="px-4 py-3 text-xs text-gray-500">
                    {cc.sentAt ? tt.dateTime(cc.sentAt) : '—'}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Paginación */}
      {totalPages > 1 && (
        <div className="mt-3 flex items-center justify-between text-xs text-gray-500">
          <span>{total} contactos · página {page} de {totalPages}</span>
          <div className="flex gap-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="rounded border border-gray-300 px-3 py-1 text-xs hover:bg-gray-50 disabled:opacity-40"
            >
              Anterior
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page === totalPages}
              className="rounded border border-gray-300 px-3 py-1 text-xs hover:bg-gray-50 disabled:opacity-40"
            >
              Siguiente
            </button>
          </div>
        </div>
      )}

      {/* Modal — Mensaje enviado al contacto */}
      {messageContact && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
          onClick={() => setMessageContact(null)}
        >
          <div
            className="w-full max-w-2xl overflow-hidden rounded-lg bg-white shadow-xl"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3">
              <div className="flex items-center gap-2">
                <MessageSquare className="h-4 w-4 text-blue-600" />
                <h3 className="text-sm font-semibold text-gray-900">
                  Mensaje enviado
                </h3>
              </div>
              <button
                onClick={() => setMessageContact(null)}
                className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
                title="Cerrar"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
            <div className="px-5 py-4 text-sm">
              <div className="mb-3 flex items-baseline justify-between gap-3 text-xs text-gray-500">
                <span><strong className="text-gray-900">{messageContact.clientName ?? '—'}</strong> · <span className="font-mono">{messageContact.phoneNumber}</span></span>
                {messageContact.sentAt && (
                  <span>{tt.dateTime(messageContact.sentAt)}</span>
                )}
              </div>
              <pre className="max-h-[60vh] overflow-y-auto whitespace-pre-wrap rounded-md border border-gray-200 bg-gray-50 px-4 py-3 font-sans text-sm text-gray-800">
                {messageContact.generatedMessage}
              </pre>
            </div>
            <div className="flex justify-end gap-2 border-t border-gray-200 bg-gray-50 px-5 py-3">
              <button
                onClick={() => setMessageContact(null)}
                className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
              >
                Cerrar
              </button>
            </div>
          </div>
        </div>
      )}

      <ToastContainer toasts={toasts} onRemove={remove} />
    </div>
  )
}
