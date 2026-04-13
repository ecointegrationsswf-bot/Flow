import { useState } from 'react'
import { Webhook, Mail, MessageSquare, Loader2, Zap, CheckCircle, AlertCircle } from 'lucide-react'
import { useTenantActions, useUpdateWebhookContract, type TenantAction } from '../hooks/useTenantActions'
import { WebhookBuilderModal } from '@/modules/webhookBuilder/components/WebhookBuilderModal'
import type { WebhookContractBundle } from '@/modules/webhookBuilder/types'

const FRIENDLY_NAMES: Record<string, string> = {
  'SEND_EMAIL_RESUME': 'Enviar email con resumen',
  'TRANSFER_CHAT': 'Escalar a humano',
  'SEND_MESSAGE': 'Enviar mensaje',
  'SEND_RESUME': 'Enviar resumen',
  'PREMIUM': 'Premium',
  'CLOSE_CONVERSATION': 'Cerrar conversacion',
  'ESCALATE_TO_HUMAN': 'Escalar a ejecutivo',
  'SEND_PAYMENT_LINK': 'Enviar enlace de pago',
  'SEND_DOCUMENT': 'Enviar documento',
  'GENERATE_TEST_PDF': 'Generar PDF de prueba',
  'SEND_TEST_EMAIL': 'Enviar email de prueba',
  'VALIDATE_IDENTITY': 'Validar identidad',
}

export function TenantActionsPage() {
  const { data: actions = [], isLoading } = useTenantActions()
  const updateContract = useUpdateWebhookContract()
  const [wizardActionId, setWizardActionId] = useState<string | null>(null)
  const [successId, setSuccessId] = useState<string | null>(null)

  const wizardAction = actions.find(a => a.id === wizardActionId)

  const handleSaveContract = (bundle: WebhookContractBundle) => {
    if (!wizardActionId) return
    const contract = JSON.stringify(bundle)
    updateContract.mutate(
      { id: wizardActionId, contract },
      {
        onSuccess: () => {
          setWizardActionId(null)
          setSuccessId(wizardActionId)
          setTimeout(() => setSuccessId(null), 3000)
        },
      },
    )
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-20">
        <Loader2 className="h-8 w-8 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-xl font-bold text-gray-900">Acciones</h1>
        <p className="text-sm text-gray-500">
          Configura el webhook default de cada accion. Todos los maestros de campana que usen la accion heredan esta configuracion automaticamente.
        </p>
      </div>

      {actions.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-12 text-center">
          <Zap className="mx-auto h-12 w-12 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">No hay acciones configuradas para este tenant.</p>
          <p className="text-xs text-gray-400 mt-1">El administrador debe crear las acciones desde el panel de admin.</p>
        </div>
      ) : (
        <div className="grid gap-4">
          {actions.map(action => (
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

      {/* Webhook Builder Modal */}
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

function ActionCard({ action, onConfigure, isSaving, justSaved }: {
  action: TenantAction
  onConfigure: () => void
  isSaving: boolean
  justSaved: boolean
}) {
  const friendly = FRIENDLY_NAMES[action.name] ?? action.name

  return (
    <div className="rounded-lg border border-gray-200 bg-white p-5 shadow-sm hover:shadow-md transition-shadow">
      <div className="flex items-start justify-between">
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-gray-900">{friendly}</h3>
            <span className="rounded bg-gray-100 px-2 py-0.5 text-[10px] font-mono text-gray-500">{action.name}</span>
          </div>
          {action.description && (
            <p className="mt-1 text-xs text-gray-500">{action.description}</p>
          )}

          <div className="mt-2 flex flex-wrap gap-2">
            {action.requiresWebhook && (
              <span className="inline-flex items-center gap-1 rounded-full bg-purple-50 border border-purple-200 px-2 py-0.5 text-[11px] font-medium text-purple-700">
                <Webhook className="h-3 w-3" /> Webhook
              </span>
            )}
            {action.sendsEmail && (
              <span className="inline-flex items-center gap-1 rounded-full bg-green-50 border border-green-200 px-2 py-0.5 text-[11px] font-medium text-green-700">
                <Mail className="h-3 w-3" /> Email
              </span>
            )}
            {action.sendsSms && (
              <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 border border-amber-200 px-2 py-0.5 text-[11px] font-medium text-amber-700">
                <MessageSquare className="h-3 w-3" /> SMS
              </span>
            )}
          </div>
        </div>

        <div className="flex flex-col items-end gap-2">
          {action.hasWebhookContract ? (
            <span className="inline-flex items-center gap-1 rounded-full bg-green-50 border border-green-200 px-2.5 py-1 text-xs font-medium text-green-700">
              <CheckCircle className="h-3.5 w-3.5" /> Configurado
            </span>
          ) : action.requiresWebhook ? (
            <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 border border-amber-200 px-2.5 py-1 text-xs font-medium text-amber-700">
              <AlertCircle className="h-3.5 w-3.5" /> Sin configurar
            </span>
          ) : null}

          {action.requiresWebhook && (
            <button
              onClick={onConfigure}
              disabled={isSaving}
              className="flex items-center gap-1.5 rounded-lg bg-purple-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-purple-700 disabled:opacity-50 transition-colors"
            >
              {isSaving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Zap className="h-3.5 w-3.5" />}
              {action.hasWebhookContract ? 'Editar contrato' : 'Configurar webhook'}
            </button>
          )}
        </div>
      </div>

      {justSaved && (
        <div className="mt-3 rounded-lg bg-green-50 border border-green-200 px-3 py-2 text-xs text-green-700">
          Contrato guardado. Todos los maestros que usen esta accion lo heredan automaticamente.
        </div>
      )}
    </div>
  )
}

function parseContract(json: string | null): Partial<WebhookContractBundle> {
  if (!json) return {}
  try { return JSON.parse(json) } catch { return {} }
}
