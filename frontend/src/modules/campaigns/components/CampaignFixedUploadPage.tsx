import { useState, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, Upload, FileSpreadsheet, Eye, X, AlertTriangle, CheckCircle } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { useAgents } from '@/shared/hooks/useAgents'
import {
  usePreviewFixedFormat,
  useUploadFixedFormat,
  type FixedContactPreview,
  type FixedFormatPreviewResult,
} from '@/shared/hooks/useCampaigns'

const REQUIRED_COLUMNS = ['NombreCliente', 'Celular', 'CodigoPais', 'KeyValue']

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
          <button onClick={onClose} className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors">
            <X className="h-5 w-5" />
          </button>
        </div>
        <pre className="flex-1 overflow-auto p-5 text-xs leading-relaxed text-gray-700 font-mono bg-gray-50 rounded-b-xl">
          {formatted}
        </pre>
      </div>
    </div>
  )
}

// ── Página principal ──────────────────────────────────────────────────────────
export function CampaignFixedUploadPage() {
  const navigate = useNavigate()
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [file, setFile] = useState<File | null>(null)
  const [name, setName] = useState('')
  const [agentId, setAgentId] = useState('')
  const [scheduledAt, setScheduledAt] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<FixedFormatPreviewResult | null>(null)
  const [selectedJson, setSelectedJson] = useState<FixedContactPreview | null>(null)
  const [done, setDone] = useState(false)

  const { data: agents } = useAgents()
  const previewMutation = usePreviewFixedFormat()
  const uploadMutation = useUploadFixedFormat()

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0] ?? null
    setFile(f)
    setPreview(null)
    setError(null)
    if (f && !name) setName(f.name.replace(/\.[^.]+$/, ''))
  }

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault()
    const f = e.dataTransfer.files[0]
    if (!f) return
    setFile(f)
    setPreview(null)
    setError(null)
    if (!name) setName(f.name.replace(/\.[^.]+$/, ''))
  }

  const handlePreview = () => {
    if (!file) { setError('Selecciona un archivo.'); return }
    const fd = new FormData()
    fd.append('file', file)
    previewMutation.mutate(fd, {
      onSuccess: (data) => { setPreview(data); setError(null) },
      onError: (err: unknown) => {
        const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'Error al analizar el archivo.'
        setError(msg)
      },
    })
  }

  const handleCreate = () => {
    if (!file || !name.trim() || !agentId) {
      setError('Completa todos los campos requeridos.')
      return
    }
    const fd = new FormData()
    fd.append('file', file)
    fd.append('Name', name.trim())
    fd.append('AgentId', agentId)
    if (scheduledAt) fd.append('ScheduledAt', new Date(scheduledAt).toISOString())

    uploadMutation.mutate(fd, {
      onSuccess: () => setDone(true),
      onError: (err: unknown) => {
        const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'Error al crear la campana.'
        setError(msg)
      },
    })
  }

  // ── Pantalla de éxito ──
  if (done) {
    return (
      <div>
        <PageHeader title="Campana creada" />
        <div className="mx-auto max-w-md space-y-4 text-center">
          <CheckCircle className="mx-auto h-12 w-12 text-green-500" />
          <p className="text-lg font-semibold text-gray-900">Campana creada exitosamente</p>
          <button
            onClick={() => navigate('/campaigns')}
            className="rounded-lg bg-blue-600 px-6 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
          >
            Ver campanas
          </button>
        </div>
      </div>
    )
  }

  return (
    <>
      {selectedJson && <JsonModal contact={selectedJson} onClose={() => setSelectedJson(null)} />}

      <div>
        <PageHeader
          title="Nueva campana — Formato fijo"
          subtitle="Columnas requeridas: NombreCliente, Celular, CodigoPais, KeyValue"
          action={
            <button
              onClick={() => navigate('/campaigns')}
              className="flex items-center gap-2 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
            >
              <ArrowLeft className="h-4 w-4" /> Volver
            </button>
          }
        />

        <div className="mx-auto max-w-3xl space-y-6">
          {/* Columnas requeridas */}
          <div className="rounded-lg bg-blue-50 px-4 py-3 flex flex-wrap items-center gap-2">
            <span className="text-xs font-medium text-blue-700 mr-1">Requeridas:</span>
            {REQUIRED_COLUMNS.map((col) => (
              <span key={col} className="rounded-md bg-blue-100 px-2.5 py-0.5 text-xs font-mono font-medium text-blue-800">{col}</span>
            ))}
            <span className="text-xs text-blue-600 ml-1">+ columnas extra capturadas automaticamente</span>
          </div>

          {/* Configuración */}
          <div className="rounded-lg bg-white p-5 shadow-sm space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Nombre de la campana *</label>
                <input
                  type="text"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Ej: Cobros SURA Marzo 2026"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Agente IA *</label>
                <select
                  value={agentId}
                  onChange={(e) => setAgentId(e.target.value)}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
                >
                  <option value="">Selecciona un agente...</option>
                  {agents?.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                </select>
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Fecha de inicio <span className="font-normal text-gray-500">(opcional)</span>
              </label>
              <input
                type="datetime-local"
                value={scheduledAt}
                onChange={(e) => setScheduledAt(e.target.value)}
                className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
              />
            </div>
          </div>

          {/* Drop zone */}
          <div
            onDragOver={(e) => e.preventDefault()}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
            className="flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300 bg-white p-8 hover:border-blue-400 hover:bg-blue-50 transition-colors"
          >
            <input ref={fileInputRef} type="file" accept=".xlsx,.xls,.csv" className="hidden" onChange={handleFileChange} />
            {file ? (
              <>
                <FileSpreadsheet className="h-8 w-8 text-blue-500 mb-2" />
                <p className="text-sm font-medium text-gray-800">{file.name}</p>
                <p className="text-xs text-gray-500 mt-0.5">{(file.size / 1024).toFixed(1)} KB — clic para cambiar</p>
              </>
            ) : (
              <>
                <Upload className="h-8 w-8 text-gray-400 mb-2" />
                <p className="text-sm font-medium text-gray-700">Arrastra tu archivo o haz clic para seleccionar</p>
                <p className="text-xs text-gray-500 mt-0.5">Excel (.xlsx, .xls) o CSV — max 10 MB</p>
              </>
            )}
          </div>

          {/* Botón analizar */}
          {file && !preview && (
            <button
              onClick={handlePreview}
              disabled={previewMutation.isPending}
              className="flex w-full items-center justify-center gap-2 rounded-lg border border-blue-600 px-4 py-2.5 text-sm font-medium text-blue-600 hover:bg-blue-50 disabled:opacity-50 transition-colors"
            >
              {previewMutation.isPending
                ? <><span className="h-4 w-4 animate-spin rounded-full border-2 border-blue-600 border-t-transparent" /> Analizando...</>
                : 'Analizar archivo'}
            </button>
          )}

          {error && (
            <div className="rounded-lg border border-red-200 bg-red-50 p-3">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          )}

          {/* Preview de contactos */}
          {preview && (
            <div className="rounded-lg bg-white shadow-sm overflow-hidden">
              <div className="flex items-center justify-between px-5 py-3 border-b border-gray-200">
                <div>
                  <p className="text-sm font-semibold text-gray-900">
                    {preview.contacts.length} contactos únicos — {preview.totalRowsRead} filas leídas
                  </p>
                  {preview.extraColumns.length > 0 && (
                    <p className="text-xs text-gray-500 mt-0.5">
                      Columnas extra: {preview.extraColumns.join(', ')}
                    </p>
                  )}
                </div>
                <button
                  onClick={() => { setPreview(null); setFile(null); if (fileInputRef.current) fileInputRef.current.value = '' }}
                  className="text-xs text-gray-400 hover:text-gray-600 transition-colors"
                >
                  Cambiar archivo
                </button>
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
                        <td className="px-4 py-2.5 text-gray-600 max-w-[200px] truncate" title={c.keyValue}>{c.keyValue}</td>
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
          )}

          {/* Crear campaña */}
          {preview && (
            <button
              onClick={handleCreate}
              disabled={uploadMutation.isPending || !name.trim() || !agentId}
              className="flex w-full items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
            >
              {uploadMutation.isPending
                ? <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" /> Creando...</>
                : 'Crear campana'}
            </button>
          )}
        </div>
      </div>
    </>
  )
}
