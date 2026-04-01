import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, Check, Loader2, Eye, X, AlertTriangle } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { useCampaignTemplates } from '@/shared/hooks/useCampaignTemplates'
import {
  usePreviewFixedFormat,
  useUploadFixedFormat,
  type FixedContactPreview,
  type FixedFormatPreviewResult,
} from '@/shared/hooks/useCampaigns'
import { FileDropZone } from './FileDropZone'

const STEPS = [
  { number: 1, label: 'Configuracion' },
  { number: 2, label: 'Vista previa' },
  { number: 3, label: 'Confirmacion' },
]

// ── Modal JSON ────────────────────────────────────────────────────────────────
function JsonModal({ contact, onClose }: { contact: FixedContactPreview; onClose: () => void }) {
  let formatted = contact.contactDataJson
  try { formatted = JSON.stringify(JSON.parse(contact.contactDataJson), null, 2) } catch { }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="flex max-h-[80vh] w-full max-w-2xl flex-col rounded-xl bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-4">
          <div>
            <p className="font-semibold text-gray-900">{contact.nombreCliente}</p>
            <p className="text-xs text-gray-500">{contact.phone} — {contact.totalRegistros} registro(s)</p>
          </div>
          <button
            onClick={onClose}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
          >
            <X className="h-5 w-5" />
          </button>
        </div>
        <pre className="flex-1 overflow-auto rounded-b-xl bg-gray-50 p-5 font-mono text-xs leading-relaxed text-gray-700">
          {formatted}
        </pre>
      </div>
    </div>
  )
}

// ── Página principal ──────────────────────────────────────────────────────────
export function CampaignUploadPage() {
  const navigate = useNavigate()
  const [step, setStep] = useState(1)
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<FixedFormatPreviewResult | null>(null)
  const [selectedTemplateId, setSelectedTemplateId] = useState('')
  const [startDate, setStartDate] = useState(new Date().toISOString().slice(0, 16))
  const [error, setError] = useState<string | null>(null)
  const [selectedJson, setSelectedJson] = useState<FixedContactPreview | null>(null)

  const { data: templates } = useCampaignTemplates()
  const previewMutation = usePreviewFixedFormat()
  const createMutation = useUploadFixedFormat()

  const activeTemplates = templates?.filter((t) => t.isActive) ?? []
  const selectedTemplate = activeTemplates.find((t) => t.id === selectedTemplateId)

  const handleStep1Submit = () => {
    if (!file || !selectedTemplateId || !selectedTemplate) {
      setError('Selecciona un maestro de campana y un archivo.')
      return
    }
    setError(null)
    const formData = new FormData()
    formData.append('file', file)

    previewMutation.mutate(formData, {
      onSuccess: (result) => { setPreview(result); setStep(2) },
      onError: (err: unknown) => {
        const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
          ?? 'Error al analizar el archivo.'
        setError(msg)
      },
    })
  }

  const handleCreate = () => {
    if (!file || !selectedTemplate) return
    const formData = new FormData()
    formData.append('file', file)
    formData.append('Name', selectedTemplate.name)
    formData.append('AgentId', selectedTemplate.agentDefinitionId)
    formData.append('CampaignTemplateId', selectedTemplate.id)
    if (startDate) formData.append('ScheduledAt', new Date(startDate).toISOString())

    createMutation.mutate(formData, {
      onSuccess: () => navigate('/campaigns'),
    })
  }

  const inputClass = 'mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500'

  return (
    <>
      {selectedJson && <JsonModal contact={selectedJson} onClose={() => setSelectedJson(null)} />}

      <div className="mx-auto max-w-3xl">
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
                  <select value={selectedTemplateId} onChange={(e) => setSelectedTemplateId(e.target.value)} className={inputClass}>
                    <option value="">-- Seleccionar maestro --</option>
                    {activeTemplates.map((t) => (
                      <option key={t.id} value={t.id}>{t.name} — {t.agentName}</option>
                    ))}
                  </select>
                </div>

                {selectedTemplate && (
                  <div className="rounded-md bg-blue-50 p-3 text-xs text-blue-700 space-y-1">
                    <p>Agente: <strong>{selectedTemplate.agentName}</strong></p>
                    <p>Seguimientos: {selectedTemplate.followUpHours.length > 0 ? selectedTemplate.followUpHours.map((h) => `${h}h`).join(', ') : 'Ninguno'}</p>
                    <p>Cierre automatico: {selectedTemplate.autoCloseHours}h</p>
                    {selectedTemplate.sendEmail && <p>Email: {selectedTemplate.emailAddress}</p>}
                  </div>
                )}

                <div>
                  <label className="block text-sm font-medium text-gray-700">Fecha de inicio</label>
                  <input type="datetime-local" value={startDate} onChange={(e) => setStartDate(e.target.value)} className={inputClass} />
                  <p className="mt-1 text-xs text-gray-500">Por defecto la fecha actual.</p>
                </div>
              </div>
            </section>

            <section className="rounded-lg bg-white p-5 shadow-sm">
              <h2 className="mb-4 text-sm font-semibold text-gray-900">Archivo de contactos</h2>
              <FileDropZone accept=".csv,.xlsx,.xls" onFileSelect={setFile} selectedFile={file} onClear={() => setFile(null)} />
            </section>

            <div className="flex justify-end gap-3">
              <button
                type="button"
                onClick={() => navigate('/campaigns')}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
              >
                Cancelar
              </button>
              <button
                onClick={handleStep1Submit}
                disabled={!file || !selectedTemplateId || previewMutation.isPending}
                className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
              >
                {previewMutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
                {previewMutation.isPending ? 'Analizando...' : 'Siguiente'}
              </button>
            </div>
          </div>
        )}

        {/* Step 2: Preview de contactos */}
        {step === 2 && preview && (
          <div className="space-y-6">
            <div className="rounded-lg bg-white shadow-sm overflow-hidden">
              <div className="flex items-center justify-between px-5 py-3 border-b border-gray-200">
                <p className="text-sm font-semibold text-gray-900">
                  {preview.contacts.length} contactos únicos — {preview.totalRowsRead} filas leídas
                </p>
                {preview.extraColumns.length > 0 && (
                  <p className="text-xs text-gray-500">
                    Extra: {preview.extraColumns.join(', ')}
                  </p>
                )}
              </div>

              {preview.warnings.length > 0 && (
                <div className="flex items-start gap-2 border-b border-amber-200 bg-amber-50 px-5 py-3">
                  <AlertTriangle className="h-4 w-4 shrink-0 text-amber-500 mt-0.5" />
                  <ul className="space-y-0.5">
                    {preview.warnings.map((w, i) => (
                      <li key={i} className="text-xs text-amber-700">{w}</li>
                    ))}
                  </ul>
                </div>
              )}

              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-100 text-sm">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-4 py-2.5 text-left text-xs font-medium text-gray-500">NombreCliente</th>
                      <th className="px-4 py-2.5 text-left text-xs font-medium text-gray-500">Celular (E.164)</th>
                      <th className="px-4 py-2.5 text-left text-xs font-medium text-gray-500">KeyValue</th>
                      <th className="px-4 py-2.5 text-center text-xs font-medium text-gray-500">Registros</th>
                      <th className="px-4 py-2.5 text-center text-xs font-medium text-gray-500">JSON</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {preview.contacts.map((c, i) => (
                      <tr key={i} className="hover:bg-gray-50">
                        <td className="px-4 py-2.5 font-medium text-gray-900">{c.nombreCliente}</td>
                        <td className="px-4 py-2.5 font-mono text-gray-700">{c.phone}</td>
                        <td className="max-w-[180px] truncate px-4 py-2.5 text-gray-600" title={c.keyValue}>{c.keyValue}</td>
                        <td className="px-4 py-2.5 text-center">
                          <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${c.totalRegistros > 1 ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-600'}`}>
                            {c.totalRegistros}
                          </span>
                        </td>
                        <td className="px-4 py-2.5 text-center">
                          <button
                            onClick={() => setSelectedJson(c)}
                            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                            title="Ver JSON generado"
                          >
                            <Eye className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="flex justify-between">
              <button
                type="button"
                onClick={() => setStep(1)}
                className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
              >
                <ArrowLeft className="h-4 w-4" /> Atras
              </button>
              <button
                onClick={() => setStep(3)}
                className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
              >
                Continuar
              </button>
            </div>
          </div>
        )}

        {/* Step 3: Confirmation */}
        {step === 3 && preview && selectedTemplate && (
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
                  <dt className="text-gray-500">Contactos unicos</dt>
                  <dd className="font-medium text-gray-900">{preview.contacts.length}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-gray-500">Filas leidas</dt>
                  <dd className="font-medium text-gray-900">{preview.totalRowsRead}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-gray-500">Inicio</dt>
                  <dd className="font-medium text-gray-900">{startDate ? new Date(startDate).toLocaleString('es-PA') : 'Inmediato'}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-gray-500">Seguimientos</dt>
                  <dd className="font-medium text-gray-900">{selectedTemplate.followUpHours.map((h) => `${h}h`).join(', ') || 'Ninguno'}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-gray-500">Cierre</dt>
                  <dd className="font-medium text-gray-900">{selectedTemplate.autoCloseHours}h</dd>
                </div>
              </dl>
            </section>

            <div className="flex justify-between">
              <button
                type="button"
                onClick={() => setStep(2)}
                className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
              >
                <ArrowLeft className="h-4 w-4" /> Atras
              </button>
              <button
                onClick={handleCreate}
                disabled={createMutation.isPending}
                className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
              >
                {createMutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
                {createMutation.isPending ? 'Creando...' : 'Crear campana'}
              </button>
            </div>
          </div>
        )}
      </div>
    </>
  )
}
