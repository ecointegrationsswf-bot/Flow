import { useNavigate, useParams } from 'react-router-dom'
import { useForm, Controller } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { useAgent, useCreateAgent, useUpdateAgent } from '@/shared/hooks/useAgents'
import { useWhatsAppLines } from '@/shared/hooks/useWhatsAppLines'
import { agentSchema, agentDefaults, type AgentFormData } from '../schemas/agentSchema'
import type { ChannelType } from '@/shared/types'

const channelOptions: ChannelType[] = ['WhatsApp', 'Email', 'Sms']

export function AgentFormPage() {
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const navigate = useNavigate()

  const { data: existing, isLoading } = useAgent(id)
  const { data: whatsAppLines } = useWhatsAppLines()
  const createMutation = useCreateAgent()
  const updateMutation = useUpdateAgent()

  const { register, handleSubmit, control, watch, formState: { errors } } = useForm<AgentFormData>({
    resolver: zodResolver(agentSchema),
    values: isEdit && existing
      ? {
          name: existing.name,
          type: existing.type,
          isActive: existing.isActive,
          systemPrompt: existing.systemPrompt,
          tone: existing.tone,
          language: existing.language,
          avatarName: existing.avatarName,
          enabledChannels: existing.enabledChannels,
          sendFrom: existing.sendFrom,
          sendUntil: existing.sendUntil,
          maxRetries: existing.maxRetries,
          retryIntervalHours: existing.retryIntervalHours,
          inactivityCloseHours: existing.inactivityCloseHours,
          closeConditionKeyword: existing.closeConditionKeyword,
          llmModel: existing.llmModel,
          temperature: existing.temperature,
          maxTokens: existing.maxTokens,
          whatsAppLineId: existing.whatsAppLineId,
        }
      : agentDefaults,
  })

  const temperature = watch('temperature')
  const promptLength = watch('systemPrompt')?.length ?? 0

  const onSubmit = (data: AgentFormData) => {
    // Convertir string vacio a null para whatsAppLineId
    const payload = { ...data, whatsAppLineId: data.whatsAppLineId || null }
    if (isEdit && id) {
      updateMutation.mutate({ id, ...payload }, { onSuccess: () => navigate('/agents') })
    } else {
      createMutation.mutate(payload, { onSuccess: () => navigate('/agents') })
    }
  }

  if (isEdit && isLoading) return <LoadingSpinner />

  const isPending = createMutation.isPending || updateMutation.isPending

  return (
    <div className="mx-auto max-w-3xl">
      <PageHeader
        title={isEdit ? 'Editar agente' : 'Nuevo agente'}
        action={
          <button
            onClick={() => navigate('/agents')}
            className="flex items-center gap-1.5 text-sm text-gray-600 hover:text-gray-900"
          >
            <ArrowLeft className="h-4 w-4" /> Volver
          </button>
        }
      />

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
        {/* Seccion 1: Identidad */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Identidad del agente</h2>
          <div className="grid grid-cols-2 gap-4">
            <div className="col-span-2 sm:col-span-1">
              <label className="block text-sm font-medium text-gray-700">Nombre *</label>
              <input {...register('name')} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500" />
              {errors.name && <p className="mt-1 text-xs text-red-600">{errors.name.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Tipo *</label>
              <select {...register('type')} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500">
                <option value="Cobros">Cobros</option>
                <option value="Reclamos">Reclamos</option>
                <option value="Renovaciones">Renovaciones</option>
                <option value="General">General</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Nombre avatar</label>
              <input {...register('avatarName')} placeholder="Ej: Sofia" className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Tono</label>
              <select {...register('tone')} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500">
                <option value="amigable">Amigable</option>
                <option value="formal">Formal</option>
                <option value="neutro">Neutro</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Idioma</label>
              <select {...register('language')} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500">
                <option value="es">Espanol</option>
                <option value="en">Ingles</option>
              </select>
            </div>
            <div className="flex items-center gap-2">
              <input type="checkbox" id="isActive" {...register('isActive')} className="h-4 w-4 rounded border-gray-300 text-blue-600" />
              <label htmlFor="isActive" className="text-sm font-medium text-gray-700">Activo</label>
            </div>
          </div>
        </section>

        {/* Seccion 3: Canales y horario */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Canales y horario</h2>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Canales habilitados *</label>
              <Controller
                name="enabledChannels"
                control={control}
                render={({ field }) => (
                  <div className="mt-2 flex gap-4">
                    {channelOptions.map((ch) => (
                      <label key={ch} className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={field.value.includes(ch)}
                          onChange={(e) => {
                            if (e.target.checked) field.onChange([...field.value, ch])
                            else field.onChange(field.value.filter((v) => v !== ch))
                          }}
                          className="h-4 w-4 rounded border-gray-300 text-blue-600"
                        />
                        <span className="text-sm text-gray-700">{ch}</span>
                      </label>
                    ))}
                  </div>
                )}
              />
              {errors.enabledChannels && <p className="mt-1 text-xs text-red-600">{errors.enabledChannels.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Linea de WhatsApp</label>
              <select {...register('whatsAppLineId')} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500">
                <option value="">Sin asignar</option>
                {whatsAppLines?.map((line) => (
                  <option key={line.id} value={line.id}>
                    {line.displayName} {line.phoneNumber ? `(${line.phoneNumber})` : ''} — Instancia: {line.instanceId}
                  </option>
                ))}
              </select>
              <p className="mt-1 text-xs text-gray-500">El numero de WhatsApp que usara este agente para enviar mensajes</p>
            </div>
          </div>
        </section>

        {/* Seccion 4: Config LLM — solo editable desde el portal de admin */}

        {/* Actions */}
        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={() => navigate('/agents')}
            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={isPending}
            className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
          >
            {isPending ? 'Guardando...' : isEdit ? 'Actualizar agente' : 'Crear agente'}
          </button>
        </div>
      </form>
    </div>
  )
}
