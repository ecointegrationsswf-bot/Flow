import { AlertTriangle, X, FileText, Zap, CircleDot } from 'lucide-react'
import type { AssignmentConflict } from '@/modules/admin/hooks/useAdminTenantAssignments'

interface Props {
  kind: 'prompts' | 'actions'
  conflicts: AssignmentConflict[]
  tenantName: string
  onClose: () => void
}

/**
 * Modal mostrado cuando el super admin intenta desasignar prompts/acciones
 * que están en uso por maestros de campaña del tenant. Enumera los maestros
 * bloqueantes y explica que el cliente debe removerlos antes de desasignar.
 */
export function AssignmentConflictModal({ kind, conflicts, tenantName, onClose }: Props) {
  const label = kind === 'prompts' ? 'prompts' : 'acciones'
  const LabelIcon = kind === 'prompts' ? FileText : Zap
  const iconColor = kind === 'prompts' ? 'text-indigo-500' : 'text-amber-500'

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/60 p-4">
      <div className="flex max-h-[80vh] w-full max-w-xl flex-col overflow-hidden rounded-xl bg-white shadow-2xl">
        <div className="flex items-start justify-between border-b border-amber-200 bg-amber-50 px-6 py-4">
          <div className="flex items-start gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-amber-100">
              <AlertTriangle className="h-5 w-5 text-amber-600" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-amber-900">
                No es posible desasignar {label} en uso
              </h2>
              <p className="mt-0.5 text-xs text-amber-800">
                El cliente <span className="font-medium">{tenantName}</span> tiene{' '}
                {conflicts.length} maestro(s) de campaña que referencian {label} que
                intentas desasignar.
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="rounded-lg p-1.5 text-amber-700 hover:bg-amber-100"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-6 py-4">
          <p className="mb-3 text-sm text-gray-700">
            Para continuar, ingresá al tenant del cliente y remové estos {label} en los
            siguientes maestros de campaña. Luego volvé a intentar la desasignación.
          </p>

          <ul className="space-y-2">
            {conflicts.map((c) => (
              <li
                key={c.templateId}
                className="flex items-start gap-3 rounded-lg border border-gray-200 bg-white p-3"
              >
                <LabelIcon className={`mt-0.5 h-4 w-4 shrink-0 ${iconColor}`} />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate text-sm font-medium text-gray-900">{c.templateName}</p>
                    <span
                      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-medium ${
                        c.isActive
                          ? 'bg-green-100 text-green-700'
                          : 'bg-gray-100 text-gray-600'
                      }`}
                    >
                      <CircleDot className="h-2.5 w-2.5" />
                      {c.isActive ? 'Activo' : 'Inactivo'}
                    </span>
                  </div>
                  <p className="mt-0.5 text-xs text-gray-500">
                    {c.usedIds.length} {label} en uso en este maestro.
                  </p>
                </div>
              </li>
            ))}
          </ul>
        </div>

        <div className="border-t border-gray-200 bg-gray-50 px-6 py-3">
          <div className="flex justify-end">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg bg-gray-900 px-4 py-1.5 text-sm font-medium text-white hover:bg-gray-800"
            >
              Entendido
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
