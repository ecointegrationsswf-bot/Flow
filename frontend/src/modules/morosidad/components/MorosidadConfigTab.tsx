import { useEffect, useState } from 'react'
import { Save, Loader2, Info } from 'lucide-react'
import {
  useDelinquencyConfig,
  useUpsertDelinquencyConfig,
  type DelinquencyConfigPayload,
} from '../hooks/useMorosidad'
import { useCampaignTemplates } from '@/shared/hooks/useCampaignTemplates'

interface Props {
  actionId: string
}

const COUNTRY_CODES = [
  { code: '507', label: 'Panamá (+507)' },
  { code: '57',  label: 'Colombia (+57)' },
  { code: '52',  label: 'México (+52)' },
  { code: '58',  label: 'Venezuela (+58)' },
  { code: '51',  label: 'Perú (+51)' },
  { code: '593', label: 'Ecuador (+593)' },
  { code: '1',   label: 'EE.UU./Canadá (+1)' },
]

const DEFAULT_FORM: DelinquencyConfigPayload = {
  codigoPais: '507',
  itemsJsonPath: '',
  autoCrearCampanas: false,
  campaignTemplateId: null,
  agentDefinitionId: null,
  campaignNamePattern: '{accion} {fecha}',
  notificationEmail: '',
  isActive: true,
}

export function MorosidadConfigTab({ actionId }: Props) {
  const { data: config, isLoading } = useDelinquencyConfig(actionId)
  const upsert = useUpsertDelinquencyConfig(actionId)
  const { data: templates = [] } = useCampaignTemplates()

  const [form, setForm] = useState<DelinquencyConfigPayload>(DEFAULT_FORM)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (config) {
      setForm({
        codigoPais:          config.codigoPais || '507',
        itemsJsonPath:       config.itemsJsonPath || '',
        autoCrearCampanas:   config.autoCrearCampanas,
        campaignTemplateId:  config.campaignTemplateId,
        agentDefinitionId:   config.agentDefinitionId,
        campaignNamePattern: config.campaignNamePattern || '{accion} {fecha}',
        notificationEmail:   config.notificationEmail || '',
        isActive:            config.isActive,
      })
    }
  }, [config])

  const handleSave = async () => {
    await upsert.mutateAsync({
      ...form,
      itemsJsonPath:      form.itemsJsonPath?.trim() || null,
      notificationEmail:  form.notificationEmail?.trim() || null,
      campaignNamePattern: form.campaignNamePattern?.trim() || null,
    })
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Info banner si no hay config aún */}
      {!config && (
        <div className="flex items-start gap-2 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-700">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          <p>Esta acción no tiene configuración de morosidad todavía. Completa el formulario para activarla.</p>
        </div>
      )}

      <div className="grid grid-cols-2 gap-6">
        {/* Código de país */}
        <div className="space-y-1.5">
          <label className="block text-sm font-medium text-gray-700">
            Código de país <span className="text-red-500">*</span>
          </label>
          <select
            value={form.codigoPais}
            onChange={(e) => setForm({ ...form, codigoPais: e.target.value })}
            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          >
            {COUNTRY_CODES.map((c) => (
              <option key={c.code} value={c.code}>{c.label}</option>
            ))}
          </select>
          <p className="text-xs text-gray-500">
            Se agrega como prefijo si el teléfono del payload no lo incluye.
          </p>
        </div>

        {/* Items JSON Path */}
        <div className="space-y-1.5">
          <label className="block text-sm font-medium text-gray-700">
            Items JSON Path
          </label>
          <input
            type="text"
            value={form.itemsJsonPath ?? ''}
            onChange={(e) => setForm({ ...form, itemsJsonPath: e.target.value })}
            placeholder="$.data  o  $.resultado.items"
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
          <p className="text-xs text-gray-500">
            Ruta al array de registros dentro del payload. Vacío = la raíz es el array.
          </p>
        </div>
      </div>

      {/* Auto-crear campañas */}
      <div className="rounded-lg border border-gray-200 p-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-gray-800">Crear campañas automáticamente</p>
            <p className="mt-0.5 text-xs text-gray-500">
              Si está activo, el sistema crea una campaña de WhatsApp por cada grupo de contacto
              al terminar de procesar el payload.
            </p>
          </div>
          <button
            type="button"
            onClick={() => setForm({ ...form, autoCrearCampanas: !form.autoCrearCampanas })}
            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none ${
              form.autoCrearCampanas ? 'bg-blue-600' : 'bg-gray-300'
            }`}
          >
            <span
              className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${
                form.autoCrearCampanas ? 'translate-x-6' : 'translate-x-1'
              }`}
            />
          </button>
        </div>

        {form.autoCrearCampanas && (
          <div className="mt-4 space-y-4 border-t border-gray-100 pt-4">
            {/* Maestro de campaña */}
            <div className="space-y-1.5">
              <label className="block text-sm font-medium text-gray-700">
                Maestro de campaña <span className="text-red-500">*</span>
              </label>
              <select
                value={form.campaignTemplateId ?? ''}
                onChange={(e) =>
                  setForm({ ...form, campaignTemplateId: e.target.value || null })
                }
                className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none"
              >
                <option value="">— Seleccionar —</option>
                {templates.map((t) => (
                  <option key={t.id} value={t.id}>{t.name}</option>
                ))}
              </select>
              <p className="text-xs text-gray-500">El agente IA se hereda del maestro de campaña.</p>
            </div>

            {/* Patrón de nombre */}
            <div className="space-y-1.5">
              <label className="block text-sm font-medium text-gray-700">
                Patrón de nombre de campaña
              </label>
              <input
                type="text"
                value={form.campaignNamePattern ?? ''}
                onChange={(e) => setForm({ ...form, campaignNamePattern: e.target.value })}
                placeholder="{accion} {fecha}"
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none"
              />
              <p className="text-xs text-gray-500">
                Placeholders: <code className="rounded bg-gray-100 px-1">{'{accion}'}</code>{' '}
                <code className="rounded bg-gray-100 px-1">{'{fecha}'}</code>{' '}
                <code className="rounded bg-gray-100 px-1">{'{grupos}'}</code>
              </p>
            </div>
          </div>
        )}
      </div>

      {/* Notificación manual */}
      {!form.autoCrearCampanas && (
        <div className="space-y-1.5">
          <label className="block text-sm font-medium text-gray-700">
            Email de notificación (modo manual)
          </label>
          <input
            type="email"
            value={form.notificationEmail ?? ''}
            onChange={(e) => setForm({ ...form, notificationEmail: e.target.value })}
            placeholder="ejecutivo@empresa.com"
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none"
          />
          <p className="text-xs text-gray-500">
            Recibirá un email cuando el sistema genere nuevos grupos de contacto para revisión.
          </p>
        </div>
      )}

      {/* Estado activo */}
      <div className="flex items-center gap-3">
        <input
          id="isActive"
          type="checkbox"
          checked={form.isActive}
          onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
          className="h-4 w-4 rounded border-gray-300 text-blue-600"
        />
        <label htmlFor="isActive" className="text-sm text-gray-700">
          Configuración activa (el procesador usará esta config al recibir datos)
        </label>
      </div>

      {/* Botón guardar */}
      <div className="flex items-center gap-3 border-t border-gray-100 pt-4">
        <button
          onClick={handleSave}
          disabled={upsert.isPending}
          className="flex items-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-60"
        >
          {upsert.isPending ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <Save className="h-4 w-4" />
          )}
          Guardar configuración
        </button>
        {saved && (
          <span className="text-sm font-medium text-green-600">✓ Guardado</span>
        )}
        {upsert.isError && (
          <span className="text-sm text-red-600">Error al guardar</span>
        )}
      </div>
    </div>
  )
}
