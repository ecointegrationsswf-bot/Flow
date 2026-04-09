import { useState } from 'react'
import { Plus, Pencil, Trash2, Star, ToggleLeft, ToggleRight, X, Loader2, Brain } from 'lucide-react'
import {
  useAgentRegistry,
  useAvailableTemplatesForRegistry,
  useCreateRegistryEntry,
  useUpdateRegistryEntry,
  useToggleRegistryEntry,
  useDeleteRegistryEntry,
  type AgentRegistryEntry,
  type AgentRegistryPayload,
} from '../hooks/useAgentRegistry'

const emptyForm: AgentRegistryPayload = {
  slug: '',
  name: '',
  capabilities: '',
  campaignTemplateId: '',
  isWelcome: false,
}

export function BrainAgentRegistryPage() {
  const { data: entries = [], isLoading } = useAgentRegistry()
  const { data: templates = [] } = useAvailableTemplatesForRegistry()
  const createMut = useCreateRegistryEntry()
  const updateMut = useUpdateRegistryEntry()
  const toggleMut = useToggleRegistryEntry()
  const deleteMut = useDeleteRegistryEntry()

  const [showModal, setShowModal] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<AgentRegistryPayload>(emptyForm)
  const [error, setError] = useState('')

  const openCreate = () => {
    setEditingId(null)
    setForm(emptyForm)
    setError('')
    setShowModal(true)
  }

  const openEdit = (e: AgentRegistryEntry) => {
    setEditingId(e.id)
    setForm({
      slug: e.slug,
      name: e.name,
      capabilities: e.capabilities,
      campaignTemplateId: e.campaignTemplateId,
      isWelcome: e.isWelcome,
    })
    setError('')
    setShowModal(true)
  }

  const handleSave = async () => {
    if (!form.slug.trim() || !form.name.trim() || !form.campaignTemplateId) {
      setError('Slug, nombre y maestro de campana son obligatorios')
      return
    }
    try {
      if (editingId) {
        await updateMut.mutateAsync({ id: editingId, data: form })
      } else {
        await createMut.mutateAsync(form)
      }
      setShowModal(false)
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Error al guardar')
    }
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Eliminar este agente del registro?')) return
    await deleteMut.mutateAsync(id)
  }

  const isSaving = createMut.isPending || updateMut.isPending

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-20">
        <Loader2 className="h-8 w-8 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Cerebro — Registro de Agentes</h1>
          <p className="text-sm text-gray-500">Catalogo de agentes para el clasificador inteligente del Cerebro</p>
        </div>
        <button
          onClick={openCreate}
          className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Nuevo agente
        </button>
      </div>

      {entries.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-12 text-center">
          <Brain className="mx-auto h-12 w-12 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">No hay agentes en el registro del Cerebro</p>
          <p className="mt-1 text-xs text-gray-400">Registra agentes para que el Cerebro pueda clasificar y rutear mensajes automaticamente.</p>
          <button onClick={openCreate} className="mt-4 text-sm font-medium text-blue-600 hover:text-blue-700">
            Registrar primer agente
          </button>
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-gray-200 bg-gray-50">
                <th className="px-4 py-3 font-medium text-gray-600">Slug</th>
                <th className="px-4 py-3 font-medium text-gray-600">Nombre</th>
                <th className="px-4 py-3 font-medium text-gray-600">Capacidades</th>
                <th className="px-4 py-3 font-medium text-gray-600">Maestro</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Welcome</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Estado</th>
                <th className="px-4 py-3 text-right font-medium text-gray-600">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {entries.map((e) => (
                <tr key={e.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 py-3">
                    <span className="inline-flex rounded bg-indigo-50 px-2 py-0.5 text-xs font-mono font-semibold text-indigo-700">{e.slug}</span>
                  </td>
                  <td className="px-4 py-3 font-medium text-gray-900">{e.name}</td>
                  <td className="px-4 py-3 text-gray-600 max-w-xs truncate text-xs">{e.capabilities}</td>
                  <td className="px-4 py-3">
                    <div>
                      <p className="text-xs font-medium text-gray-700">{e.campaignTemplateName ?? '-'}</p>
                      {e.agentName && <p className="text-[10px] text-gray-400">Agente: {e.agentName}</p>}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-center">
                    {e.isWelcome ? (
                      <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">
                        <Star className="h-3 w-3" /> Welcome
                      </span>
                    ) : (
                      <span className="text-xs text-gray-400">-</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    <button onClick={() => toggleMut.mutate(e.id)} className="transition-colors" title={e.isActive ? 'Desactivar' : 'Activar'}>
                      {e.isActive ? <ToggleRight className="h-6 w-6 text-green-500" /> : <ToggleLeft className="h-6 w-6 text-gray-300" />}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-1">
                      <button onClick={() => openEdit(e)} className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors">
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button onClick={() => handleDelete(e.id)} className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors">
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto p-4 bg-black/50">
          <div className="w-full max-w-lg rounded-xl bg-white shadow-xl my-auto">
            <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
              <h2 className="text-lg font-semibold text-gray-900">
                {editingId ? 'Editar agente' : 'Nuevo agente'}
              </h2>
              <button onClick={() => setShowModal(false)} className="text-gray-400 hover:text-gray-600">
                <X className="h-5 w-5" />
              </button>
            </div>

            <div className="space-y-4 px-6 py-5">
              {error && <div className="rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">{error}</div>}

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="mb-1 block text-sm font-medium text-gray-700">Slug *</label>
                  <input
                    value={form.slug}
                    onChange={(e) => setForm({ ...form, slug: e.target.value.toLowerCase().replace(/[^a-z0-9-_]/g, '') })}
                    placeholder="cobros"
                    className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  />
                  <p className="mt-1 text-[10px] text-gray-400">Identificador interno unico. El Cerebro lo usa para rutear mensajes. Sin espacios. Ej: cobros, soporte-tecnico, ventas</p>
                </div>
                <div>
                  <label className="mb-1 block text-sm font-medium text-gray-700">Nombre *</label>
                  <input
                    value={form.name}
                    onChange={(e) => setForm({ ...form, name: e.target.value })}
                    placeholder="Agente de Cobros"
                    className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  />
                </div>
              </div>

              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">Capacidades (lenguaje natural) *</label>
                <textarea
                  value={form.capabilities}
                  onChange={(e) => setForm({ ...form, capabilities: e.target.value })}
                  rows={3}
                  placeholder="Gestiona cobros de seguros, negocia pagos, informa saldos pendientes y fechas de vencimiento."
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
                <p className="mt-1 text-[10px] text-gray-400">Describe que temas maneja este agente y que palabras suelen usar los clientes cuando necesitan este servicio. Mientras mas especifico, mejor rutea el Cerebro.</p>
              </div>

              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">Maestro de campana asociado *</label>
                <select
                  value={form.campaignTemplateId}
                  onChange={(e) => setForm({ ...form, campaignTemplateId: e.target.value })}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                >
                  <option value="">Selecciona un maestro de campana</option>
                  {templates.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name} {t.agentName ? `· Agente: ${t.agentName}` : ''} {t.labelCount > 0 ? `· ${t.labelCount} etiquetas` : ''}
                    </option>
                  ))}
                </select>
              </div>

              <div className="flex items-center gap-3 rounded-lg border border-gray-200 px-4 py-3">
                <button
                  type="button"
                  onClick={() => setForm({ ...form, isWelcome: !form.isWelcome })}
                  className="transition-colors"
                >
                  {form.isWelcome ? <ToggleRight className="h-7 w-7 text-amber-500" /> : <ToggleLeft className="h-7 w-7 text-gray-300" />}
                </button>
                <div>
                  <p className="text-sm font-medium text-gray-800">Agente Welcome</p>
                  <p className="text-xs text-gray-500">Primer punto de contacto para mensajes nuevos (uno por tenant)</p>
                </div>
              </div>
            </div>

            <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
              <button onClick={() => setShowModal(false)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">
                Cancelar
              </button>
              <button
                onClick={handleSave}
                disabled={isSaving}
                className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
              >
                {isSaving && <Loader2 className="h-4 w-4 animate-spin" />}
                {editingId ? 'Guardar cambios' : 'Registrar agente'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
