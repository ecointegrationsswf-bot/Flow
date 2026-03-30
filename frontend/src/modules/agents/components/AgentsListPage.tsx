import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Bot, Plus, Pencil, Trash2, Phone, AlertCircle } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { Badge } from '@/shared/components/Badge'
import { EmptyState } from '@/shared/components/EmptyState'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { useAgents, useDeleteAgent } from '@/shared/hooks/useAgents'

export function AgentsListPage() {
  const { data: agents, isLoading } = useAgents()
  const deleteMutation = useDeleteAgent()
  const [deleteId, setDeleteId] = useState<string | null>(null)
  const [blockMessage, setBlockMessage] = useState<string | null>(null)

  const handleDelete = async () => {
    if (!deleteId) return
    try {
      await deleteMutation.mutateAsync(deleteId)
      setDeleteId(null)
    } catch (err: any) {
      const msg = err?.response?.data?.error ?? 'No se pudo eliminar el agente.'
      setDeleteId(null)
      setBlockMessage(msg)
    }
  }

  if (isLoading) return <LoadingSpinner />

  return (
    <div>
      <PageHeader
        title="Agentes IA"
        subtitle="Configura los agentes que atienden conversaciones"
        action={
          <Link
            to="/agents/new"
            className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
          >
            <Plus className="h-4 w-4" /> Nuevo agente
          </Link>
        }
      />

      {!agents || agents.length === 0 ? (
        <EmptyState
          icon={Bot}
          title="Sin agentes configurados"
          description="Crea tu primer agente IA para empezar a gestionar conversaciones"
          action={
            <Link
              to="/agents/new"
              className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
            >
              Crear agente
            </Link>
          }
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {agents.map((agent) => (
            <div key={agent.id} className="rounded-lg bg-white p-5 shadow-sm">
              <div className="flex items-start justify-between">
                <div className="flex items-center gap-3">
                  <div className="flex h-10 w-10 items-center justify-center rounded-full bg-blue-100">
                    <Bot className="h-5 w-5 text-blue-600" />
                  </div>
                  <div>
                    <h3 className="text-sm font-semibold text-gray-900">{agent.name}</h3>
                    {agent.avatarName && (
                      <p className="text-xs text-gray-500">Persona: {agent.avatarName}</p>
                    )}
                  </div>
                </div>
                <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${agent.isActive ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                  {agent.isActive ? 'Activo' : 'Inactivo'}
                </span>
              </div>

              <div className="mt-3 flex flex-wrap gap-1.5">
                <Badge variant={agent.type}>{agent.type}</Badge>
                {agent.enabledChannels.map((ch) => (
                  <Badge key={ch} variant="General">{ch}</Badge>
                ))}
              </div>

              {/* Linea WhatsApp asociada */}
              {agent.whatsAppLineName && (
                <div className="mt-3 flex items-center gap-1.5 rounded-md bg-green-50 border border-green-200 px-2.5 py-1.5">
                  <Phone className="h-3.5 w-3.5 text-green-600" />
                  <span className="text-xs font-medium text-green-700">{agent.whatsAppLineName}</span>
                  {agent.whatsAppLinePhone && (
                    <span className="text-xs text-green-600">({agent.whatsAppLinePhone})</span>
                  )}
                </div>
              )}

              <div className="mt-3 space-y-1 text-xs text-gray-500">
                <p>Modelo: {agent.llmModel} | Temp: {agent.temperature}</p>
                <p>Horario: {agent.sendFrom ?? '—'} a {agent.sendUntil ?? '—'}</p>
                <p>Reintentos: {agent.maxRetries} | Cierre: {agent.inactivityCloseHours}h</p>
              </div>

              <div className="mt-4 flex justify-end gap-1 border-t border-gray-100 pt-3">
                <Link
                  to={`/agents/${agent.id}/edit`}
                  className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                  title="Editar"
                >
                  <Pencil className="h-4 w-4" />
                </Link>
                <button
                  onClick={() => setDeleteId(agent.id)}
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

      {/* Confirmar eliminación */}
      <ConfirmDialog
        open={!!deleteId}
        onClose={() => setDeleteId(null)}
        onConfirm={handleDelete}
        title="Eliminar agente"
        description="Esta accion no se puede deshacer. El agente sera eliminado permanentemente."
        confirmLabel="Eliminar"
        variant="danger"
      />

      {/* Bloqueo: agente vinculado a campaña */}
      {blockMessage && (
        <dialog
          ref={(el) => { if (el && !el.open) el.showModal() }}
          onClose={() => setBlockMessage(null)}
          className="rounded-xl p-0 backdrop:bg-black/40 shadow-xl"
        >
          <div className="w-[420px] p-6">
            <div className="flex items-start gap-3">
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-amber-100">
                <AlertCircle className="h-5 w-5 text-amber-600" />
              </div>
              <div>
                <h3 className="text-sm font-semibold text-gray-900">No se puede eliminar el agente</h3>
                <p className="mt-2 text-sm text-gray-600 leading-relaxed">{blockMessage}</p>
              </div>
            </div>
            <div className="mt-5 flex justify-end">
              <button
                onClick={() => setBlockMessage(null)}
                className="rounded-lg bg-gray-900 px-4 py-2 text-sm font-medium text-white hover:bg-gray-700 transition-colors"
              >
                Entendido
              </button>
            </div>
          </div>
        </dialog>
      )}
    </div>
  )
}
