import { useState } from 'react'
import { Search, RotateCcw, Plus, ShieldAlert, Globe, Building2, Loader2, X } from 'lucide-react'
import {
  useInvalidNumbers, useRestoreInvalidNumber, useAddInvalidNumber,
  type InvalidNumber,
} from '@/modules/admin/hooks/useInvalidNumbers'
import { confirmDialog } from '@/shared/components/dialog'

const SOURCE_LABELS: Record<string, { label: string; className: string }> = {
  'ultramsg-precheck': { label: 'Pre-check UltraMsg', className: 'bg-purple-100 text-purple-700' },
  'dispatch-error':    { label: 'Error de envío',     className: 'bg-red-100 text-red-700' },
  'manual':            { label: 'Registro manual',    className: 'bg-blue-100 text-blue-700' },
  'campaign-import':   { label: 'Carga de campaña',   className: 'bg-amber-100 text-amber-700' },
}

export function InvalidNumbersPage() {
  const [q, setQ] = useState('')
  const [source, setSource] = useState<string>('')
  const [showInactive, setShowInactive] = useState(false)
  const [page, setPage] = useState(1)
  const pageSize = 50

  const { data, isLoading } = useInvalidNumbers({
    q: q.trim() || undefined,
    source: source || undefined,
    isActive: showInactive ? undefined : true,
    page,
    pageSize,
  })

  const restore = useRestoreInvalidNumber()
  const add = useAddInvalidNumber()

  const [addModalOpen, setAddModalOpen] = useState(false)
  const [newPhone, setNewPhone] = useState('')
  const [newReason, setNewReason] = useState('')
  const [newIsGlobal, setNewIsGlobal] = useState(false)

  const handleRestore = async (item: InvalidNumber) => {
    const ok = await confirmDialog({
      title: `¿Restaurar ${item.phoneNumber}?`,
      description: 'Volverá a ser elegible para próximas campañas.',
    })
    if (!ok) return
    restore.mutate(item.id)
  }

  const handleAdd = () => {
    if (!newPhone.trim()) return
    add.mutate(
      { phoneNumber: newPhone.trim(), reason: newReason.trim() || undefined, isGlobal: newIsGlobal },
      { onSuccess: () => { setAddModalOpen(false); setNewPhone(''); setNewReason(''); setNewIsGlobal(false) } },
    )
  }

  const total = data?.total ?? 0
  const items = data?.items ?? []

  return (
    <div className="px-6 py-4">
      <div className="mb-4 flex items-center justify-between">
        <div>
          <h1 className="flex items-center gap-2 text-xl font-bold text-gray-900">
            <ShieldAlert className="h-5 w-5 text-red-600" /> Números sin WhatsApp
          </h1>
          <p className="text-xs text-gray-500">
            Lista negra de teléfonos detectados como inválidos por UltraMsg <code>/contacts/check</code>,
            errores recurrentes de envío, o agregados manualmente. Bloquea el envío en futuras campañas.
          </p>
        </div>
        <button
          onClick={() => setAddModalOpen(true)}
          className="flex items-center gap-1 rounded-lg bg-red-600 px-3 py-2 text-xs font-medium text-white hover:bg-red-700"
        >
          <Plus className="h-3.5 w-3.5" /> Agregar manualmente
        </button>
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative flex-1 min-w-[240px]">
          <Search className="absolute left-2 top-2.5 h-4 w-4 text-gray-400" />
          <input
            type="text"
            placeholder="Buscar por teléfono o motivo..."
            value={q}
            onChange={(e) => { setQ(e.target.value); setPage(1) }}
            className="w-full rounded-lg border border-gray-300 pl-8 pr-2 py-2 text-xs"
          />
        </div>
        <select
          value={source}
          onChange={(e) => { setSource(e.target.value); setPage(1) }}
          className="rounded-lg border border-gray-300 px-2 py-2 text-xs"
        >
          <option value="">Todos los orígenes</option>
          <option value="ultramsg-precheck">Pre-check UltraMsg</option>
          <option value="dispatch-error">Error de envío</option>
          <option value="manual">Registro manual</option>
          <option value="campaign-import">Carga de campaña</option>
        </select>
        <label className="flex items-center gap-1 text-xs text-gray-700">
          <input
            type="checkbox"
            checked={showInactive}
            onChange={(e) => { setShowInactive(e.target.checked); setPage(1) }}
          />
          Mostrar restaurados
        </label>
        <div className="ml-auto text-xs text-gray-500">{total} resultado{total === 1 ? '' : 's'}</div>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
        </div>
      ) : items.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-12 text-center">
          <ShieldAlert className="mx-auto h-10 w-10 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">No hay números en la lista negra.</p>
        </div>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white">
          <table className="w-full text-xs">
            <thead className="bg-gray-50 text-left text-gray-600">
              <tr>
                <th className="px-3 py-2">Teléfono</th>
                <th className="px-3 py-2">Origen</th>
                <th className="px-3 py-2">Alcance</th>
                <th className="px-3 py-2">Motivo</th>
                <th className="px-3 py-2 text-center">Intentos</th>
                <th className="px-3 py-2">Primera detección</th>
                <th className="px-3 py-2">Última detección</th>
                <th className="px-3 py-2">Estado</th>
                <th className="px-3 py-2"></th>
              </tr>
            </thead>
            <tbody>
              {items.map((item: InvalidNumber) => {
                const src = SOURCE_LABELS[item.source] ?? { label: item.source, className: 'bg-gray-100 text-gray-600' }
                return (
                  <tr key={item.id} className="border-t border-gray-100 hover:bg-gray-50">
                    <td className="px-3 py-2 font-mono text-gray-900">{item.phoneNumber}</td>
                    <td className="px-3 py-2">
                      <span className={`rounded-full px-2 py-0.5 text-[10px] font-medium ${src.className}`}>
                        {src.label}
                      </span>
                    </td>
                    <td className="px-3 py-2">
                      {item.scope === 'global' ? (
                        <span className="inline-flex items-center gap-1 text-purple-700">
                          <Globe className="h-3 w-3" /> Cross-tenant
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-blue-700">
                          <Building2 className="h-3 w-3" /> Solo tenant
                        </span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-gray-700">{item.reason}</td>
                    <td className="px-3 py-2 text-center font-mono">{item.occurrenceCount}</td>
                    <td className="px-3 py-2 text-gray-500">{new Date(item.firstDetectedAt).toLocaleString('es-PA')}</td>
                    <td className="px-3 py-2 text-gray-500">{new Date(item.lastCheckedAt).toLocaleString('es-PA')}</td>
                    <td className="px-3 py-2">
                      {item.isActive ? (
                        <span className="rounded-full bg-red-100 px-2 py-0.5 text-[10px] font-medium text-red-700">
                          Activo
                        </span>
                      ) : (
                        <span className="rounded-full bg-gray-100 px-2 py-0.5 text-[10px] font-medium text-gray-500">
                          Restaurado
                        </span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-right">
                      {item.isActive && (
                        <button
                          onClick={() => handleRestore(item)}
                          disabled={restore.isPending}
                          className="inline-flex items-center gap-1 rounded border border-gray-300 px-2 py-1 text-[11px] font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                          title="Restaurar — volver a habilitar este número"
                        >
                          <RotateCcw className="h-3 w-3" /> Restaurar
                        </button>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {total > pageSize && (
        <div className="mt-3 flex items-center justify-between text-xs text-gray-600">
          <span>Página {page} de {Math.ceil(total / pageSize)}</span>
          <div className="flex items-center gap-1">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="rounded border border-gray-300 px-2 py-1 hover:bg-gray-50 disabled:opacity-40"
            >Anterior</button>
            <button
              onClick={() => setPage((p) => p + 1)}
              disabled={page * pageSize >= total}
              className="rounded border border-gray-300 px-2 py-1 hover:bg-gray-50 disabled:opacity-40"
            >Siguiente</button>
          </div>
        </div>
      )}

      {addModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
          <div className="w-full max-w-md rounded-xl bg-white p-5 shadow-xl">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-base font-semibold text-gray-900">Agregar número a lista negra</h2>
              <button onClick={() => setAddModalOpen(false)} className="text-gray-400 hover:text-gray-600">
                <X className="h-5 w-5" />
              </button>
            </div>
            <div className="space-y-3">
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Teléfono (E.164)</label>
                <input
                  type="text"
                  value={newPhone}
                  onChange={(e) => setNewPhone(e.target.value)}
                  placeholder="+50760001234"
                  className="w-full rounded border border-gray-300 px-2 py-1.5 text-xs font-mono"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Motivo (opcional)</label>
                <textarea
                  value={newReason}
                  onChange={(e) => setNewReason(e.target.value)}
                  placeholder="ej: Cliente solicitó no recibir más mensajes"
                  rows={2}
                  className="w-full rounded border border-gray-300 px-2 py-1.5 text-xs"
                />
              </div>
              <label className="flex items-start gap-2 text-xs text-gray-700">
                <input
                  type="checkbox"
                  checked={newIsGlobal}
                  onChange={(e) => setNewIsGlobal(e.target.checked)}
                  className="mt-0.5"
                />
                <div>
                  <div>Elevar a global (aplicar a todos los tenants)</div>
                  <div className="text-[10px] text-gray-500 mt-0.5">
                    Por default el bloqueo es solo para este tenant. Activar solo si confirmaste
                    que el número está fuera de WhatsApp a nivel global (línea cerrada, baja oficial, etc.).
                  </div>
                </div>
              </label>
            </div>
            <div className="mt-4 flex justify-end gap-2">
              <button
                onClick={() => setAddModalOpen(false)}
                className="rounded border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
              >Cancelar</button>
              <button
                onClick={handleAdd}
                disabled={!newPhone.trim() || add.isPending}
                className="rounded bg-red-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-red-700 disabled:opacity-50"
              >
                {add.isPending ? 'Guardando...' : 'Agregar'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
