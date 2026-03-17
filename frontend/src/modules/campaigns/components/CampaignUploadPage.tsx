import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { ArrowLeft } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { useAgents } from '@/shared/hooks/useAgents'
import { useUploadCampaign } from '@/shared/hooks/useCampaigns'
import { FileDropZone } from './FileDropZone'

const campaignSchema = z.object({
  name: z.string().min(1, 'El nombre es requerido'),
  agentId: z.string().min(1, 'Selecciona un agente'),
  channel: z.enum(['WhatsApp', 'Email', 'Sms']).default('WhatsApp'),
  scheduledAt: z.string().optional(),
})

type CampaignForm = z.infer<typeof campaignSchema>

export function CampaignUploadPage() {
  const navigate = useNavigate()
  const [file, setFile] = useState<File | null>(null)
  const { data: agents } = useAgents()
  const uploadMutation = useUploadCampaign()

  const { register, handleSubmit, formState: { errors } } = useForm<CampaignForm>({
    resolver: zodResolver(campaignSchema),
    defaultValues: { channel: 'WhatsApp' },
  })

  const onSubmit = (data: CampaignForm) => {
    if (!file) return
    const formData = new FormData()
    formData.append('file', file)
    formData.append('Name', data.name)
    formData.append('AgentId', data.agentId)
    if (data.scheduledAt) formData.append('ScheduledAt', data.scheduledAt)

    uploadMutation.mutate(formData, { onSuccess: () => navigate('/campaigns') })
  }

  return (
    <div className="mx-auto max-w-2xl">
      <PageHeader
        title="Nueva campana"
        action={
          <button
            onClick={() => navigate('/campaigns')}
            className="flex items-center gap-1.5 text-sm text-gray-600 hover:text-gray-900"
          >
            <ArrowLeft className="h-4 w-4" /> Volver
          </button>
        }
      />

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Detalles de la campana</h2>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Nombre *</label>
              <input
                {...register('name')}
                placeholder="Ej: Cobros Marzo 2026 - Sura"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
              {errors.name && <p className="mt-1 text-xs text-red-600">{errors.name.message}</p>}
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700">Agente *</label>
                <select
                  {...register('agentId')}
                  className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                >
                  <option value="">Seleccionar agente</option>
                  {agents?.map((a) => (
                    <option key={a.id} value={a.id}>{a.name} ({a.type})</option>
                  ))}
                </select>
                {errors.agentId && <p className="mt-1 text-xs text-red-600">{errors.agentId.message}</p>}
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">Canal</label>
                <select
                  {...register('channel')}
                  className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                >
                  <option value="WhatsApp">WhatsApp</option>
                  <option value="Email">Email</option>
                  <option value="Sms">SMS</option>
                </select>
              </div>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Fecha programada (opcional)</label>
              <input
                type="datetime-local"
                {...register('scheduledAt')}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
              <p className="mt-1 text-xs text-gray-500">Deja vacio para iniciar inmediatamente</p>
            </div>
          </div>
        </section>

        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">Archivo de contactos</h2>
          <FileDropZone
            accept=".csv,.xlsx,.xls"
            onFileSelect={setFile}
            selectedFile={file}
            onClear={() => setFile(null)}
          />
        </section>

        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={() => navigate('/campaigns')}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={!file || uploadMutation.isPending}
            className="rounded-md bg-blue-600 px-6 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {uploadMutation.isPending ? 'Subiendo...' : 'Crear campana'}
          </button>
        </div>
      </form>
    </div>
  )
}
