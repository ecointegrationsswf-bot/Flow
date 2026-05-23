import { useState } from 'react'
import { Webhook, Mail, MessageSquare, Loader2, Zap, CheckCircle, AlertCircle, Eye } from 'lucide-react'
import { useTenantActions, type TenantAction } from '../hooks/useTenantActions'
import { WebhookBuilderModal } from '@/modules/webhookBuilder/components/WebhookBuilderModal'
import { parseContract } from '@/shared/utils/parseContract'

const FRIENDLY_NAMES: Record<string, string> = {
  'SEND_EMAIL_RESUME': 'Enviar email con resumen',
  'TRANSFER_CHAT': 'Escalar a humano',
  'SEND_MESSAGE': 'Enviar mensaje',
  'SEND_RESUME': 'Enviar resumen',
  'PREMIUM': 'Premium',
  'CLOSE_CONVERSATION': 'Cerrar conversación',
  'ESCALATE_TO_HUMAN': 'Escalar a ejecutivo',
  'SEND_PAYMENT_LINK': 'Enviar enlace de pago',
  'SEND_DOCUMENT': 'Enviar documento',
  'GENERATE_TEST_PDF': 'Generar PDF de prueba',
  'SEND_TEST_EMAIL': 'Enviar email de prueba',
  'VALIDATE_IDENTITY': 'Validar identidad',
}

export function TenantActionsPage() {
  const { data: actions = [], isLoading } = useTenantActions()
  // Modo solo-consulta para el tenant: el contrato del webhook lo configura
  // EXCLUSIVAMENTE el super admin desde "Editar Cliente → Webhooks". Esta
  // pantalla solo permite VER el contrato actual sin posibilidad de modificarlo.
  // El backend (TenantActionsController) también devuelve 403 si recibe un PUT.
  const [wizardActionId, setWizardActionId] = useState<string | null>(null)

  const wizardAction = actions.find(a => a.id === wizardActionId)

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
          Consulta las acciones disponibles y el webhook default que configuró el administrador.
          Si necesitas modificar el contrato de alguna, solicítaselo al super admin.
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
              onView={() => setWizardActionId(action.id)}
            />
          ))}
        </div>
      )}

      {/* Webhook Builder Modal — modo solo consulta (readOnly).
          El tenant solo puede ver el contrato. La edición está bloqueada
          en frontend (botón "Cerrar" en vez de "Guardar") y en backend
          (TenantActionsController.UpdateWebhookContract devuelve 403). */}
      {wizardAction && (
        <WebhookBuilderModal
          initial={parseContract(wizardAction.defaultWebhookContract)}
          actionName={wizardAction.name}
          availableSlugs={actions
            .filter((a) => a.requiresWebhook && a.id !== wizardAction.id)
            .map((a) => a.name)}
          onClose={() => setWizardActionId(null)}
          onSave={() => setWizardActionId(null) /* no-op en readOnly */}
          readOnly
        />
      )}
    </div>
  )
}

function ActionCard({ action, onView }: {
  action: TenantAction
  onView: () => void
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

          {action.requiresWebhook && action.hasWebhookContract ? (
            <button
              onClick={onView}
              className="flex items-center gap-1.5 rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 transition-colors"
            >
              <Eye className="h-3.5 w-3.5" />
              Ver contrato
            </button>
          ) : action.requiresWebhook ? (
            <span className="text-[11px] text-gray-400 italic">
              Solicita configuración al administrador
            </span>
          ) : null}
        </div>
      </div>
    </div>
  )
}

