import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, Check, Loader2 } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { useCampaignTemplates } from '@/shared/hooks/useCampaignTemplates'
import {
  useParseCampaignFile,
  useCreateCampaignFromFile,
  type ParseResult,
} from '@/shared/hooks/useCampaigns'
import { FileDropZone } from './FileDropZone'
import { ColumnMappingStep } from './ColumnMappingStep'

const STEPS = [
  { number: 1, label: 'Configuracion' },
  { number: 2, label: 'Mapeo de columnas' },
  { number: 3, label: 'Confirmacion' },
]

export function CampaignUploadPage() {
  const navigate = useNavigate()
  const [step, setStep] = useState(1)
  const [file, setFile] = useState<File | null>(null)
  const [parseResult, setParseResult] = useState<ParseResult | null>(null)
  const [columnMapping, setColumnMapping] = useState<Record<string, string> | null>(null)
  const [selectedTemplateId, setSelectedTemplateId] = useState('')
  const [startDate, setStartDate] = useState(new Date().toISOString().slice(0, 16))
  const [error, setError] = useState<string | null>(null)

  const { data: templates } = useCampaignTemplates()
  const parseMutation = useParseCampaignFile()
  const createMutation = useCreateCampaignFromFile()

  const activeTemplates = templates?.filter(t => t.isActive) ?? []
  const selectedTemplate = activeTemplates.find(t => t.id === selectedTemplateId)

  const handleStep1Submit = () => {
    if (!file || !selectedTemplateId || !selectedTemplate) {
      setError('Selecciona un maestro de campana y un archivo.')
      return
    }
    setError(null)
    const formData = new FormData()
    formData.append('file', file)
    formData.append('Name', selectedTemplate.name)
    formData.append('AgentId', selectedTemplate.agentDefinitionId)

    parseMutation.mutate(formData, {
      onSuccess: (result) => { setParseResult(result); setStep(2) },
      onError: () => setError('Error al analizar el archivo.'),
    })
  }

  const handleMappingComplete = (mapping: Record<string, string>) => {
    setColumnMapping(mapping)
    setStep(3)
  }

  const handleCreate = () => {
    if (!parseResult || !columnMapping || !selectedTemplate) return

    createMutation.mutate(
      {
        name: selectedTemplate.name,
        agentId: selectedTemplate.agentDefinitionId,
        channel: 'WhatsApp',
        scheduledAt: startDate || undefined,
        tempFilePath: parseResult.tempFilePath,
        columnMapping,
        campaignTemplateId: selectedTemplateId,
      },
      { onSuccess: () => navigate('/campaigns') },
    )
  }

  const inputClass = "mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"

  return (
    <div className="mx-auto max-w-2xl">
      <PageHeader
        title="Nueva campana"
        action={
          <button onClick={() => navigate('/campaigns')} className="flex items-center gap-1.5 text-sm text-gray-600 hover:text-gray-900">
            <ArrowLeft className="h-4 w-4" /> Volver
          </button>
        }
      />

      {/* Step indicators */}
      <div className="mb-6 flex items-center justify-center gap-2">
        {STEPS.map(({ number, label }, idx) => (
          <div key={number} className="flex items-center gap-2">
            <div className="flex items-center gap-1.5">
              <div className={`flex h-7 w-7 items-center justify-center rounded-full text-xs font-semibold ${
                step > number ? 'bg-green-500 text-white' : step === number ? 'bg-blue-600 text-white' : 'bg-gray-200 text-gray-500'
              }`}>
                {step > number ? <Check className="h-3.5 w-3.5" /> : number}
              </div>
              <span className={`text-xs font-medium ${step >= number ? 'text-gray-900' : 'text-gray-400'}`}>{label}</span>
            </div>
            {idx < STEPS.length - 1 && <div className={`h-px w-8 ${step > number ? 'bg-green-400' : 'bg-gray-200'}`} />}
          </div>
        ))}
      </div>

      {error && <div className="mb-4 rounded-md bg-red-50 p-3 text-center text-sm text-red-600">{error}</div>}

      {/* Step 1: Select template + file + date */}
      {step === 1 && (
        <div className="space-y-6">
          <section className="rounded-lg bg-white p-5 shadow-sm">
            <h2 className="mb-4 text-sm font-semibold text-gray-900">Configuracion</h2>
            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700">Maestro de campana *</label>
                <select value={selectedTemplateId} onChange={e => setSelectedTemplateId(e.target.value)} className={inputClass}>
                  <option value="">-- Seleccionar maestro --</option>
                  {activeTemplates.map(t => (
                    <option key={t.id} value={t.id}>{t.name} — {t.agentName}</option>
                  ))}
                </select>
              </div>

              {selectedTemplate && (
                <div className="rounded-md bg-blue-50 p-3 text-xs text-blue-700 space-y-1">
                  <p>Agente: <strong>{selectedTemplate.agentName}</strong></p>
                  <p>Seguimientos: {selectedTemplate.followUpHours.length > 0 ? selectedTemplate.followUpHours.map(h => `${h}h`).join(', ') : 'Ninguno'}</p>
                  <p>Cierre automatico: {selectedTemplate.autoCloseHours}h</p>
                  {selectedTemplate.sendEmail && <p>Email: {selectedTemplate.emailAddress}</p>}
                </div>
              )}

              <div>
                <label className="block text-sm font-medium text-gray-700">Fecha de inicio</label>
                <input type="datetime-local" value={startDate} onChange={e => setStartDate(e.target.value)} className={inputClass} />
                <p className="mt-1 text-xs text-gray-500">Por defecto la fecha actual.</p>
              </div>
            </div>
          </section>

          <section className="rounded-lg bg-white p-5 shadow-sm">
            <h2 className="mb-4 text-sm font-semibold text-gray-900">Archivo de contactos</h2>
            <FileDropZone accept=".csv,.xlsx,.xls" onFileSelect={setFile} selectedFile={file} onClear={() => setFile(null)} />
          </section>

          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => navigate('/campaigns')} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">Cancelar</button>
            <button
              onClick={handleStep1Submit}
              disabled={!file || !selectedTemplateId || parseMutation.isPending}
              className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
            >
              {parseMutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              {parseMutation.isPending ? 'Analizando...' : 'Siguiente'}
            </button>
          </div>
        </div>
      )}

      {/* Step 2: Column mapping */}
      {step === 2 && parseResult && (
        <ColumnMappingStep
          columns={parseResult.columns}
          previewRows={parseResult.previewRows}
          onMappingComplete={handleMappingComplete}
          onBack={() => setStep(1)}
        />
      )}

      {/* Step 3: Confirmation */}
      {step === 3 && parseResult && columnMapping && selectedTemplate && (
        <div className="space-y-6">
          <section className="rounded-lg bg-white p-5 shadow-sm">
            <h2 className="mb-4 text-sm font-semibold text-gray-900">Resumen</h2>
            <dl className="space-y-3 text-sm">
              <div className="flex justify-between">
                <dt className="text-gray-500">Maestro</dt>
                <dd className="font-medium text-gray-900">{selectedTemplate.name}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Agente</dt>
                <dd className="font-medium text-gray-900">{selectedTemplate.agentName}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Contactos</dt>
                <dd className="font-medium text-gray-900">{parseResult.totalRows}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Inicio</dt>
                <dd className="font-medium text-gray-900">{startDate ? new Date(startDate).toLocaleString('es-PA') : 'Inmediato'}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Seguimientos</dt>
                <dd className="font-medium text-gray-900">{selectedTemplate.followUpHours.map(h => `${h}h`).join(', ') || 'Ninguno'}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Cierre</dt>
                <dd className="font-medium text-gray-900">{selectedTemplate.autoCloseHours}h</dd>
              </div>
            </dl>
          </section>

          <div className="flex justify-between">
            <button type="button" onClick={() => setStep(2)} className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">
              <ArrowLeft className="h-4 w-4" /> Atras
            </button>
            <button onClick={handleCreate} disabled={createMutation.isPending} className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors">
              {createMutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              {createMutation.isPending ? 'Creando...' : 'Crear campana'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
