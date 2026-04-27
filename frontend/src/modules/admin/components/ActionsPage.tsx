import { useState } from 'react'
import { Plus, Pencil, Trash2, Webhook, Mail, MessageSquare, ToggleLeft, ToggleRight, X, Loader2, Globe } from 'lucide-react'
import {
  useAdminActions,
  useCreateAction,
  useUpdateAction,
  useToggleAction,
  useDeleteAction,
  type ActionDefinition,
  type ActionPayload,
} from '../hooks/useAdminActions'
import { useAdminTenants } from '../hooks/useAdminTenants'

// Reusamos el catálogo único de friendly names — incluye también las acciones
// internas del Campaign Automation Worker (FOLLOW_UP_MESSAGE, AUTO_CLOSE_CAMPAIGN,
// LABEL_CONVERSATIONS) para que se muestren consistentes en toda la app.
import { getActionFriendlyName } from '@/shared/actionLabels'

const emptyForm: Omit<ActionPayload, 'tenantId'> = {
  name: '',
  description: '',
  requiresWebhook: false,
  sendsEmail: false,
  sendsSms: false,
  webhookUrl: '',
  webhookMethod: 'POST',
  defaultTriggerConfig: null,
  defaultWebhookContract: null,
}

export function ActionsPage() {
  const { data: tenants = [] } = useAdminTenants()
  const activeTenants = tenants.filter((t) => t.isActive)

  const { data: actions = [], isLoading } = useAdminActions()
  const createMut = useCreateAction()
  const updateMut = useUpdateAction()
  const toggleMut = useToggleAction()
  const deleteMut = useDeleteAction()

  const [showModal, setShowModal] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<Omit<ActionPayload, 'tenantId'>>(emptyForm)
  const [error, setError] = useState('')

  const openCreate = () => {
    setEditingId(null)
    setForm(emptyForm)
    setError('')
    setShowModal(true)
  }

  const openEdit = (a: ActionDefinition) => {
    setEditingId(a.id)
    setForm({
      name: a.name,
      description: a.description ?? '',
      requiresWebhook: a.requiresWebhook,
      sendsEmail: a.sendsEmail,
      sendsSms: a.sendsSms,
      webhookUrl: a.webhookUrl ?? '',
      webhookMethod: a.webhookMethod ?? 'POST',
      defaultTriggerConfig: a.defaultTriggerConfig,
      defaultWebhookContract: a.defaultWebhookContract,
    })
    setError('')
    setShowModal(true)
  }

  const handleSave = async () => {
    if (!form.name.trim()) {
      setError('El nombre es obligatorio')
      return
    }
    try {
      // Siempre global al crear. Al editar, el backend preserva el TenantId original.
      const payload: ActionPayload = { ...form, tenantId: null }
      if (editingId) {
        await updateMut.mutateAsync({ id: editingId, data: payload })
      } else {
        await createMut.mutateAsync(payload)
      }
      setShowModal(false)
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Error al guardar')
    }
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Eliminar esta accion?')) return
    await deleteMut.mutateAsync(id)
  }

  const isSaving = createMut.isPending || updateMut.isPending

  return (
    <div>
      {/* Header */}
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Acciones</h1>
          <p className="text-sm text-gray-500">
            Catálogo global de acciones. Asigna subconjuntos a cada cliente desde Tenants → Editar.
          </p>
        </div>
        <button
          onClick={openCreate}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Nueva accion
        </button>
      </div>

      {/* Table */}
      {isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Loader2 className="h-8 w-8 animate-spin text-gray-400" />
        </div>
      ) : actions.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-12 text-center">
          <MessageSquare className="mx-auto h-12 w-12 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">No hay acciones creadas</p>
          <button onClick={openCreate} className="mt-3 text-sm font-medium text-blue-600 hover:text-blue-700">
            Crear primera accion
          </button>
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-gray-200 bg-gray-50">
                <th className="px-4 py-3 font-medium text-gray-600">Nombre</th>
                <th className="px-4 py-3 font-medium text-gray-600">Descripcion</th>
                <th className="px-4 py-3 font-medium text-gray-600">Alcance</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Webhook</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Email</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">SMS</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Estado</th>
                <th className="px-4 py-3 text-right font-medium text-gray-600">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {actions.map((a) => (
                <tr key={a.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 py-3">
                    <div>
                      <span className="inline-flex items-center gap-1.5 rounded bg-blue-50 px-2.5 py-1 text-xs font-semibold text-blue-700">
                        {getActionFriendlyName(a.name)}
                      </span>
                      <p className="mt-0.5 text-[10px] text-gray-400">{a.name}</p>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-gray-600 max-w-xs truncate">{a.description || '\u2014'}</td>
                  <td className="px-4 py-3">
                    {a.tenantId === null ? (
                      <span className="inline-flex items-center gap-1 rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700">
                        <Globe className="h-3 w-3" /> Global
                      </span>
                    ) : (
                      <span className="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-700">
                        {activeTenants.find((t) => t.id === a.tenantId)?.name ?? a.tenantId.slice(0, 8)}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {a.requiresWebhook ? (
                      <span className="inline-flex items-center gap-1 rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-700">
                        <Webhook className="h-3 w-3" /> Si
                      </span>
                    ) : (
                      <span className="text-xs text-gray-400">No</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {a.sendsEmail ? (
                      <span className="inline-flex items-center gap-1 rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">
                        <Mail className="h-3 w-3" /> Si
                      </span>
                    ) : (
                      <span className="text-xs text-gray-400">No</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {a.sendsSms ? (
                      <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">
                        <MessageSquare className="h-3 w-3" /> Si
                      </span>
                    ) : (
                      <span className="text-xs text-gray-400">No</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    <button
                      onClick={() => toggleMut.mutate(a.id)}
                      className="transition-colors"
                      title={a.isActive ? 'Desactivar' : 'Activar'}
                    >
                      {a.isActive ? (
                        <ToggleRight className="h-6 w-6 text-green-500" />
                      ) : (
                        <ToggleLeft className="h-6 w-6 text-gray-300" />
                      )}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-1">
                      <button
                        onClick={() => openEdit(a)}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => handleDelete(a.id)}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors"
                      >
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

      {/* Modal */}
      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto p-4 bg-black/50">
          <div className="w-full max-w-lg rounded-xl bg-white shadow-xl my-auto">
            <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
              <h2 className="text-lg font-semibold text-gray-900">
                {editingId ? 'Editar accion' : 'Nueva accion global'}
              </h2>
              <button onClick={() => setShowModal(false)} className="text-gray-400 hover:text-gray-600">
                <X className="h-5 w-5" />
              </button>
            </div>

            <div className="space-y-5 px-6 py-5 overflow-y-auto max-h-[70vh]">
              {error && (
                <div className="rounded-lg bg-red-50 px-4 py-2 text-sm text-red-600">{error}</div>
              )}

              {/* Nombre */}
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">Nombre de la accion *</label>
                <select
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                >
                  <option value="">Selecciona una accion</option>
                  <option value="SEND_MESSAGE">Enviar mensaje</option>
                  <option value="SEND_RESUME">Enviar resumen</option>
                  <option value="TRANSFER_CHAT">Escalar a humano</option>
                  <option value="SEND_EMAIL_RESUME">Enviar email con resumen</option>
                  <option value="PREMIUM">Premium</option>
                  <option value="CLOSE_CONVERSATION">Cerrar conversacion</option>
                  <option value="ESCALATE_TO_HUMAN">Escalar a ejecutivo</option>
                  <option value="SEND_PAYMENT_LINK">Enviar enlace de pago</option>
                  <option value="SEND_DOCUMENT">Enviar documento</option>
                  <option value="LABEL_CONVERSATIONS">Etiquetar conversaciones</option>
                </select>
              </div>

              {/* Descripcion */}
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">Descripcion</label>
                <textarea
                  value={form.description ?? ''}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                  rows={2}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  placeholder="Describe que hace esta accion..."
                />
              </div>

              {/* Toggles */}
              <div className="space-y-3">
                <label className="mb-1 block text-sm font-medium text-gray-700">Configuracion</label>

                <div className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-3">
                  <div className="flex items-center gap-3">
                    <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-purple-100">
                      <Webhook className="h-4 w-4 text-purple-600" />
                    </div>
                    <div>
                      <p className="text-sm font-medium text-gray-800">Solicita Webhook</p>
                      <p className="text-xs text-gray-500">Envio de gestion y recepcion de datos</p>
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => setForm({ ...form, requiresWebhook: !form.requiresWebhook })}
                    className="transition-colors"
                  >
                    {form.requiresWebhook ? (
                      <ToggleRight className="h-7 w-7 text-purple-500" />
                    ) : (
                      <ToggleLeft className="h-7 w-7 text-gray-300" />
                    )}
                  </button>
                </div>

                <div className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-3">
                  <div className="flex items-center gap-3">
                    <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-green-100">
                      <Mail className="h-4 w-4 text-green-600" />
                    </div>
                    <div>
                      <p className="text-sm font-medium text-gray-800">Envia correo electronico</p>
                      <p className="text-xs text-gray-500">Notificar via email al ejecutar la accion</p>
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => setForm({ ...form, sendsEmail: !form.sendsEmail })}
                    className="transition-colors"
                  >
                    {form.sendsEmail ? (
                      <ToggleRight className="h-7 w-7 text-green-500" />
                    ) : (
                      <ToggleLeft className="h-7 w-7 text-gray-300" />
                    )}
                  </button>
                </div>

                <div className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-3">
                  <div className="flex items-center gap-3">
                    <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-amber-100">
                      <MessageSquare className="h-4 w-4 text-amber-600" />
                    </div>
                    <div>
                      <p className="text-sm font-medium text-gray-800">Envia SMS</p>
                      <p className="text-xs text-gray-500">Notificar via mensaje de texto</p>
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => setForm({ ...form, sendsSms: !form.sendsSms })}
                    className="transition-colors"
                  >
                    {form.sendsSms ? (
                      <ToggleRight className="h-7 w-7 text-amber-500" />
                    ) : (
                      <ToggleLeft className="h-7 w-7 text-gray-300" />
                    )}
                  </button>
                </div>
              </div>
            </div>

            {/* Footer */}
            <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
              <button
                onClick={() => setShowModal(false)}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
              >
                Cancelar
              </button>
              <button
                onClick={handleSave}
                disabled={isSaving}
                className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
              >
                {isSaving && <Loader2 className="h-4 w-4 animate-spin" />}
                {editingId ? 'Guardar cambios' : 'Crear accion'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
