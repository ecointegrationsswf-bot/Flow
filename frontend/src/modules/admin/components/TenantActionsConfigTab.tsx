import { useState } from 'react'
import { Webhook, Mail, MessageSquare, Cog, Loader2, Zap, CheckCircle, AlertCircle } from 'lucide-react'
import {
  useAdminTenantActionsConfig,
  useAdminUpdateTenantActionContract,
  type TenantActionConfig,
} from '@/modules/admin/hooks/useAdminTenantActionsConfig'
import { WebhookBuilderModal } from '@/modules/webhookBuilder/components/WebhookBuilderModal'
import { getActionFriendlyName } from '@/shared/actionLabels'
import type { WebhookContractBundle } from '@/modules/webhookBuilder/types'

interface Props {
  tenantId: string
}

/**
 * Tab del modal de edición de tenant que reemplaza la pantalla `/actions` que
 * estaba en el portal del cliente. El super admin configura aquí el webhook
 * default de cada acción asignada al tenant. Reusa WebhookBuilderModal y la
 * lógica visual del antiguo TenantActionsPage.
 */
export function TenantActionsConfigTab({ tenantId }: Props) {
  const { data: actions = [], isLoading } = useAdminTenantActionsConfig(tenantId)
  const updateContract = useAdminUpdateTenantActionContract(tenantId)
  const [wizardActionId, setWizardActionId] = useState<string | null>(null)
  const [successId, setSuccessId] = useState<string | null>(null)

  const wizardAction = actions.find((a) => a.id === wizardActionId)

  const handleSaveContract = (bundle: WebhookContractBundle) => {
    if (!wizardActionId) return
    updateContract.mutate(
      { actionId: wizardActionId, contract: JSON.stringify(bundle) },
      {
        onSuccess: () => {
          setSuccessId(wizardActionId)
          setWizardActionId(null)
          setTimeout(() => setSuccessId(null), 3000)
        },
      },
    )
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div className="overflow-y-auto px-6 py-4">
      <p className="mb-4 text-xs text-gray-500">
        Configura el webhook default de cada acción asignada al tenant. Todos los maestros de
        campaña que usen la acción heredan esta configuración automáticamente.
      </p>

      {actions.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-10 text-center">
          <Zap className="mx-auto h-10 w-10 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">Sin acciones de tipo webhook asignadas a este cliente.</p>
          <p className="mt-1 text-xs text-gray-400">
            Asigna acciones webhook desde la tab "Acciones asignadas".
          </p>
        </div>
      ) : (
        <div className="grid gap-3">
          {actions.map((action) => (
            <ActionCard
              key={action.id}
              action={action}
              onConfigure={() => setWizardActionId(action.id)}
              isSaving={updateContract.isPending && wizardActionId === action.id}
              justSaved={successId === action.id}
            />
          ))}
        </div>
      )}

      {wizardAction && (
        <WebhookBuilderModal
          initial={parseContract(wizardAction.defaultWebhookContract)}
          actionName={wizardAction.name}
          onClose={() => setWizardActionId(null)}
          onSave={handleSaveContract}
        />
      )}
    </div>
  )
}

function ActionCard({
  action,
  onConfigure,
  isSaving,
  justSaved,
}: {
  action: TenantActionConfig
  onConfigure: () => void
  isSaving: boolean
  justSaved: boolean
}) {
  const friendly = getActionFriendlyName(action.name)

  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm transition-shadow hover:shadow-md">
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-gray-900">{friendly}</h3>
            <span className="rounded bg-gray-100 px-2 py-0.5 text-[10px] font-mono text-gray-500">
              {action.name}
            </span>
          </div>
          {action.description && (
            <p className="mt-1 text-xs text-gray-500">{action.description}</p>
          )}

          <div className="mt-2 flex flex-wrap gap-1.5">
            {action.requiresWebhook && (
              <span className="inline-flex items-center gap-1 rounded-full border border-purple-200 bg-purple-50 px-2 py-0.5 text-[11px] font-medium text-purple-700">
                <Webhook className="h-3 w-3" /> Webhook
              </span>
            )}
            {action.sendsEmail && (
              <span className="inline-flex items-center gap-1 rounded-full border border-green-200 bg-green-50 px-2 py-0.5 text-[11px] font-medium text-green-700">
                <Mail className="h-3 w-3" /> Email
              </span>
            )}
            {action.sendsSms && (
              <span className="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700">
                <MessageSquare className="h-3 w-3" /> SMS
              </span>
            )}
            {action.isProcess && (
              <span className="inline-flex items-center gap-1 rounded-full border border-indigo-200 bg-indigo-50 px-2 py-0.5 text-[11px] font-medium text-indigo-700">
                <Cog className="h-3 w-3" /> Proceso
              </span>
            )}
          </div>
        </div>

        <div className="flex flex-col items-end gap-2">
          {action.hasWebhookContract ? (
            <span className="inline-flex items-center gap-1 rounded-full border border-green-200 bg-green-50 px-2.5 py-1 text-xs font-medium text-green-700">
              <CheckCircle className="h-3.5 w-3.5" /> Configurado
            </span>
          ) : action.requiresWebhook ? (
            <span className="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700">
              <AlertCircle className="h-3.5 w-3.5" /> Sin configurar
            </span>
          ) : null}

          {action.requiresWebhook && (
            <button
              type="button"
              onClick={onConfigure}
              disabled={isSaving}
              className="flex items-center gap-1.5 rounded-lg bg-purple-600 px-3 py-1.5 text-xs font-medium text-white transition-colors hover:bg-purple-700 disabled:opacity-50"
            >
              {isSaving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Zap className="h-3.5 w-3.5" />}
              {action.hasWebhookContract ? 'Editar contrato' : 'Configurar webhook'}
            </button>
          )}
        </div>
      </div>

      {justSaved && (
        <div className="mt-3 rounded-lg border border-green-200 bg-green-50 px-3 py-2 text-xs text-green-700">
          Contrato guardado. Todos los maestros de este cliente que usen esta acción lo heredan automáticamente.
        </div>
      )}
    </div>
  )
}

function parseContract(json: string | null): Partial<WebhookContractBundle> {
  if (!json) return {}
  try {
    return JSON.parse(json)
  } catch {
    return {}
  }
}
