import { useState, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { ClipboardList, Plus, Pencil, Trash2, Clock, Tag, Copy, Search, X } from 'lucide-react'
import {
  useCampaignTemplates,
  useDeleteCampaignTemplate,
  useDuplicateCampaignTemplate,
} from '@/shared/hooks/useCampaignTemplates'

export function CampaignTemplatesPage() {
  const navigate = useNavigate()
  const { data: templates, isLoading, isError, refetch } = useCampaignTemplates()
  const deleteMut = useDeleteCampaignTemplate()
  const duplicateMut = useDuplicateCampaignTemplate()

  // Filtros
  const [search, setSearch] = useState('')
  const [filterStatus, setFilterStatus] = useState('')

  // Estado del modal de duplicar
  const [duplicateModal, setDuplicateModal] = useState<{ id: string; name: string } | null>(null)
  const [newName, setNewName] = useState('')

  const handleDelete = async (id: string) => {
    if (!confirm('Eliminar este maestro de campana?')) return
    await deleteMut.mutateAsync(id)
  }

  const openDuplicate = (id: string, name: string) => {
    setNewName(`Copia de ${name}`)
    setDuplicateModal({ id, name })
  }

  const handleDuplicate = async () => {
    if (!duplicateModal || !newName.trim()) return
    await duplicateMut.mutateAsync({ id: duplicateModal.id, name: newName.trim() })
    setDuplicateModal(null)
  }

  const filtered = useMemo(() => {
    if (!templates) return []
    const q = search.toLowerCase().trim()
    return templates.filter((t) => {
      if (filterStatus === 'active' && !t.isActive) return false
      if (filterStatus === 'inactive' && t.isActive) return false
      if (!q) return true
      return (
        t.name.toLowerCase().includes(q) ||
        (t.agentName ?? '').toLowerCase().includes(q)
      )
    })
  }, [templates, search, filterStatus])

  const hasFilters = search || filterStatus
  const clearFilters = () => { setSearch(''); setFilterStatus('') }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Maestro de Campanas</h1>
          <p className="text-sm text-gray-500">Define las reglas y configuracion de tus campanas</p>
        </div>
        <button
          onClick={() => navigate('/campaign-templates/new')}
          className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Nuevo maestro
        </button>
      </div>

      {isLoading ? (
        <div className="py-12 text-center text-gray-400">Cargando...</div>
      ) : isError ? (
        <div className="py-12 text-center">
          <p className="text-red-500 mb-3">Error al cargar los maestros de campaña.</p>
          <button onClick={() => refetch()} className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700">Reintentar</button>
        </div>
      ) : !templates?.length ? (
        <div className="py-16 text-center">
          <ClipboardList className="mx-auto h-12 w-12 text-gray-300" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">Sin maestros de campana</h3>
          <p className="mt-1 text-sm text-gray-500">Crea tu primer maestro para definir las reglas de tus campanas.</p>
        </div>
      ) : (
        <div className="space-y-4">
          {/* Barra de filtros */}
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative flex-1 min-w-48">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 pointer-events-none" />
              <input
                type="text"
                placeholder="Buscar por nombre o agente..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="w-full rounded-lg border border-gray-300 py-2 pl-9 pr-3 text-sm text-gray-900 placeholder-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
            </div>

            <select
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value)}
              className="rounded-lg border border-gray-300 py-2 px-3 text-sm text-gray-700 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="">Todos los estados</option>
              <option value="active">Activo</option>
              <option value="inactive">Inactivo</option>
            </select>

            {hasFilters && (
              <button
                onClick={clearFilters}
                className="flex items-center gap-1 rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-600 hover:bg-gray-50 transition-colors"
              >
                <X className="h-4 w-4" /> Limpiar
              </button>
            )}

            <span className="ml-auto text-xs text-gray-400">
              {filtered.length} de {templates.length} maestro{templates.length !== 1 ? 's' : ''}
            </span>
          </div>

          {/* Grid de tarjetas */}
          {filtered.length === 0 ? (
            <div className="py-12 text-center text-sm text-gray-400">
              No se encontraron maestros con los filtros aplicados.
            </div>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {filtered.map((t) => (
                <div key={t.id} className="rounded-lg bg-white p-5 shadow-sm">
                  <div className="mb-3 flex items-start justify-between">
                    <div>
                      <h3 className="text-sm font-semibold text-gray-900">{t.name}</h3>
                      <p className="text-xs text-gray-500">Agente: {t.agentName ?? '—'}</p>
                    </div>
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${t.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                      {t.isActive ? 'Activo' : 'Inactivo'}
                    </span>
                  </div>

                  <div className="space-y-2 text-xs text-gray-600">
                    <div className="flex items-center gap-1.5">
                      <Clock className="h-3.5 w-3.5 text-gray-400" />
                      <span>Seguimientos: {t.followUpHours.length > 0 ? t.followUpHours.map(h => `${h}h`).join(', ') : 'Ninguno'}</span>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <Clock className="h-3.5 w-3.5 text-gray-400" />
                      <span>Cierre: {t.autoCloseHours}h</span>
                    </div>
                    {t.labelIds.length > 0 && (
                      <div className="flex items-center gap-1.5">
                        <Tag className="h-3.5 w-3.5 text-gray-400" />
                        <span>{t.labelIds.length} etiqueta{t.labelIds.length > 1 ? 's' : ''}</span>
                      </div>
                    )}
                    {t.sendEmail && (
                      <div className="text-blue-600">Email: {t.emailAddress}</div>
                    )}
                  </div>

                  <div className="mt-4 flex justify-end gap-1 border-t border-gray-100 pt-3">
                    <button
                      onClick={() => openDuplicate(t.id, t.name)}
                      className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-purple-600 transition-colors"
                      title="Copiar maestro"
                    >
                      <Copy className="h-4 w-4" />
                    </button>
                    <button
                      onClick={() => navigate(`/campaign-templates/${t.id}/edit`)}
                      className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                      title="Editar"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button
                      onClick={() => handleDelete(t.id)}
                      className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors"
                      title="Eliminar"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Modal — copiar maestro */}
      {duplicateModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
            <h2 className="mb-1 text-base font-semibold text-gray-900">Copiar maestro de campaña</h2>
            <p className="mb-4 text-sm text-gray-500">
              Se creará una copia de <strong>{duplicateModal.name}</strong> con toda su configuración. Puedes cambiar el nombre antes de confirmar.
            </p>
            <label className="mb-1 block text-sm font-medium text-gray-700">Nombre del nuevo maestro</label>
            <input
              autoFocus
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleDuplicate()}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-400"
              placeholder="Nombre del maestro copiado"
            />
            <div className="mt-5 flex justify-end gap-2">
              <button
                onClick={() => setDuplicateModal(null)}
                className="rounded-lg px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-100 transition-colors"
              >
                Cancelar
              </button>
              <button
                onClick={handleDuplicate}
                disabled={!newName.trim() || duplicateMut.isPending}
                className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
              >
                {duplicateMut.isPending ? 'Copiando...' : 'Crear copia'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
