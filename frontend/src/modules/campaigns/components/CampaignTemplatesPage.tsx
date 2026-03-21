import { useNavigate } from 'react-router-dom'
import { ClipboardList, Plus, Pencil, Trash2, Clock, Tag } from 'lucide-react'
import {
  useCampaignTemplates,
  useDeleteCampaignTemplate,
} from '@/shared/hooks/useCampaignTemplates'

export function CampaignTemplatesPage() {
  const navigate = useNavigate()
  const { data: templates, isLoading } = useCampaignTemplates()
  const deleteMut = useDeleteCampaignTemplate()

  const handleDelete = async (id: string) => {
    if (!confirm('Eliminar este maestro de campana?')) return
    await deleteMut.mutateAsync(id)
  }

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
      ) : !templates?.length ? (
        <div className="py-16 text-center">
          <ClipboardList className="mx-auto h-12 w-12 text-gray-300" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">Sin maestros de campana</h3>
          <p className="mt-1 text-sm text-gray-500">Crea tu primer maestro para definir las reglas de tus campanas.</p>
        </div>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {templates.map((t) => (
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
  )
}
