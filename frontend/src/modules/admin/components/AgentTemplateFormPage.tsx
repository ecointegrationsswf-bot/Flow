import { useNavigate, useParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { ArrowLeft } from 'lucide-react'
import {
  useAdminAgentTemplate,
  useCreateAgentTemplate,
  useUpdateAgentTemplate,
} from '@/modules/admin/hooks/useAdminAgentTemplates'
import { useAdminCategories } from '@/modules/admin/hooks/useAdminCategories'

const templateSchema = z.object({
  name: z.string().min(1, 'El nombre es requerido').max(100),
  category: z.string().min(1, 'La categoria es requerida'),
  isActive: z.boolean().default(true),
  systemPrompt: z.string().min(10, 'El prompt debe tener al menos 10 caracteres'),
  tone: z.string().nullable().default(null),
  language: z.string().default('es'),
  avatarName: z.string().nullable().default(null),
  sendFrom: z.string().nullable().default(null),
  sendUntil: z.string().nullable().default(null),
  maxRetries: z.coerce.number().min(1).max(10).default(3),
  retryIntervalHours: z.coerce.number().min(1).max(168).default(24),
  inactivityCloseHours: z.coerce.number().min(1).max(720).default(72),
  closeConditionKeyword: z.string().nullable().default(null),
  llmModel: z.string().default('claude-sonnet-4-6'),
  temperature: z.coerce.number().min(0).max(1).default(0.3),
  maxTokens: z.coerce.number().min(256).max(4096).default(1024),
})

type TemplateFormData = z.infer<typeof templateSchema>

const defaults: TemplateFormData = {
  name: '', category: '', isActive: true,
  systemPrompt: '', tone: 'amigable', language: 'es', avatarName: null,
  sendFrom: '08:00', sendUntil: '17:00',
  maxRetries: 3, retryIntervalHours: 24, inactivityCloseHours: 72, closeConditionKeyword: null,
  llmModel: 'claude-sonnet-4-6', temperature: 0.3, maxTokens: 1024,
}

export function AgentTemplateFormPage() {
  const { id } = useParams<{ id: string }>()
  const isEdit = !!id
  const navigate = useNavigate()

  const { data: existing, isLoading } = useAdminAgentTemplate(id)
  const { data: categories } = useAdminCategories()
  const createMut = useCreateAgentTemplate()
  const updateMut = useUpdateAgentTemplate()

  const activeCategories = categories?.filter((c) => c.isActive) ?? []

  const { register, handleSubmit, watch, formState: { errors } } = useForm<TemplateFormData>({
    resolver: zodResolver(templateSchema),
    values: isEdit && existing
      ? {
          name: existing.name, category: existing.category, isActive: existing.isActive,
          systemPrompt: existing.systemPrompt, tone: existing.tone, language: existing.language, avatarName: existing.avatarName,
          sendFrom: existing.sendFrom, sendUntil: existing.sendUntil,
          maxRetries: existing.maxRetries, retryIntervalHours: existing.retryIntervalHours,
          inactivityCloseHours: existing.inactivityCloseHours, closeConditionKeyword: existing.closeConditionKeyword,
          llmModel: existing.llmModel, temperature: existing.temperature, maxTokens: existing.maxTokens,
        }
      : defaults,
  })

  const temperature = watch('temperature')
  const promptLength = watch('systemPrompt')?.length ?? 0

  const onSubmit = (data: TemplateFormData) => {
    if (isEdit && id) {
      updateMut.mutate({ id, ...data }, { onSuccess: () => navigate('/admin/agent-templates') })
    } else {
      createMut.mutate(data, { onSuccess: () => navigate('/admin/agent-templates') })
    }
  }

  if (isEdit && isLoading) return <div className="py-12 text-center text-gray-400">Cargando...</div>

  const isPending = createMut.isPending || updateMut.isPending
  const inputClass = "mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"

  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">{isEdit ? 'Editar plantilla' : 'Nueva plantilla'}</h1>
        </div>
        <button
          onClick={() => navigate('/admin/agent-templates')}
          className="flex items-center gap-1.5 text-sm text-gray-600 hover:text-gray-900"
        >
          <ArrowLeft className="h-4 w-4" /> Volver
        </button>
      </div>

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
        {/* Seccion 1: Identidad */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Identidad del agente</h2>
          <div className="grid grid-cols-2 gap-4">
            <div className="col-span-2 sm:col-span-1">
              <label className="block text-sm font-medium text-gray-700">Nombre *</label>
              <input {...register('name')} className={inputClass} />
              {errors.name && <p className="mt-1 text-xs text-red-600">{errors.name.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Categoria *</label>
              <select {...register('category')} className={inputClass}>
                <option value="">-- Seleccionar --</option>
                {activeCategories.map((c) => (
                  <option key={c.id} value={c.name}>{c.name}</option>
                ))}
              </select>
              {errors.category && <p className="mt-1 text-xs text-red-600">{errors.category.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Nombre avatar</label>
              <input {...register('avatarName')} placeholder="Ej: Sofia" className={inputClass} />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Tono</label>
              <select {...register('tone')} className={inputClass}>
                <option value="amigable">Amigable</option>
                <option value="formal">Formal</option>
                <option value="neutro">Neutro</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Idioma</label>
              <select {...register('language')} className={inputClass}>
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

        {/* Seccion 2: Prompt */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Prompt del sistema</h2>
          <textarea
            {...register('systemPrompt')}
            rows={8}
            placeholder="Instrucciones para el agente IA..."
            className={inputClass}
          />
          <div className="mt-1 flex items-center justify-between">
            {errors.systemPrompt && <p className="text-xs text-red-600">{errors.systemPrompt.message}</p>}
            <p className="ml-auto text-xs text-gray-400">{promptLength} caracteres</p>
          </div>
          <p className="mt-2 text-xs text-gray-500">
            Define la personalidad, objetivos y restricciones del agente. El agente usara este prompt como contexto para todas sus respuestas.
          </p>
        </section>

        {/* Seccion 3: Horario */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Horario de envio</h2>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Enviar desde</label>
              <input type="time" {...register('sendFrom')} className={inputClass} />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Enviar hasta</label>
              <input type="time" {...register('sendUntil')} className={inputClass} />
            </div>
          </div>
        </section>

        {/* Seccion 4: Comportamiento */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Comportamiento</h2>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Max reintentos</label>
              <input type="number" {...register('maxRetries')} min={1} max={10} className={inputClass} />
              {errors.maxRetries && <p className="mt-1 text-xs text-red-600">{errors.maxRetries.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Intervalo reintentos (horas)</label>
              <input type="number" {...register('retryIntervalHours')} min={1} max={168} className={inputClass} />
              {errors.retryIntervalHours && <p className="mt-1 text-xs text-red-600">{errors.retryIntervalHours.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Cierre por inactividad (horas)</label>
              <input type="number" {...register('inactivityCloseHours')} min={1} max={720} className={inputClass} />
              {errors.inactivityCloseHours && <p className="mt-1 text-xs text-red-600">{errors.inactivityCloseHours.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Palabra clave de cierre</label>
              <input {...register('closeConditionKeyword')} placeholder="Ej: pago, compromiso" className={inputClass} />
            </div>
          </div>
        </section>

        {/* Seccion 5: Config LLM */}
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Configuracion LLM</h2>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Modelo</label>
              <select {...register('llmModel')} className={inputClass}>
                <option value="claude-sonnet-4-6">Claude Sonnet 4.6</option>
                <option value="claude-haiku-4-5-20251001">Claude Haiku 4.5</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Max tokens</label>
              <input type="number" {...register('maxTokens')} min={256} max={4096} className={inputClass} />
              {errors.maxTokens && <p className="mt-1 text-xs text-red-600">{errors.maxTokens.message}</p>}
            </div>
            <div className="col-span-2">
              <label className="block text-sm font-medium text-gray-700">
                Temperatura: <span className="font-normal text-blue-600">{temperature}</span>
              </label>
              <input
                type="range"
                {...register('temperature')}
                min={0} max={1} step={0.1}
                className="mt-2 w-full accent-blue-600"
              />
              <div className="flex justify-between text-xs text-gray-400">
                <span>0 - Preciso</span>
                <span>1 - Creativo</span>
              </div>
            </div>
          </div>
        </section>

        {/* Actions */}
        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={() => navigate('/admin/agent-templates')}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={isPending}
            className="rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
          >
            {isPending ? 'Guardando...' : isEdit ? 'Actualizar plantilla' : 'Crear plantilla'}
          </button>
        </div>
      </form>
    </div>
  )
}
