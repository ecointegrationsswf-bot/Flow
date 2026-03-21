import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Loader2, Save } from 'lucide-react'
import {
  useAdminPrompt,
  useCreatePrompt,
  useUpdatePrompt,
  type PromptPayload,
} from '../hooks/useAdminPrompts'
import { useAdminCategories } from '../hooks/useAdminCategories'

const emptyForm: PromptPayload = {
  name: '',
  description: '',
  categoryId: null,
  systemPrompt: '',
  resultPrompt: '',
  analysisPrompts: '',
  fieldMapping: '',
}

export function PromptFormPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const isEdit = !!id

  const { data: existing, isLoading: loadingPrompt } = useAdminPrompt(id ?? null)
  const { data: categories = [] } = useAdminCategories()
  const createMut = useCreatePrompt()
  const updateMut = useUpdatePrompt()

  const [form, setForm] = useState<PromptPayload>(emptyForm)
  const [error, setError] = useState('')

  useEffect(() => {
    if (existing) {
      setForm({
        name: existing.name,
        description: existing.description ?? '',
        categoryId: existing.categoryId,
        systemPrompt: existing.systemPrompt ?? '',
        resultPrompt: existing.resultPrompt ?? '',
        analysisPrompts: existing.analysisPrompts ?? '',
        fieldMapping: existing.fieldMapping ?? '',
      })
    }
  }, [existing])

  const handleSave = async () => {
    if (!form.name.trim()) { setError('El nombre es obligatorio'); return }
    setError('')
    try {
      const payload = { ...form, categoryId: form.categoryId || null }
      if (isEdit) {
        await updateMut.mutateAsync({ id: id!, data: payload })
      } else {
        await createMut.mutateAsync(payload)
      }
      navigate('/admin/prompts')
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Error al guardar')
    }
  }

  const isSaving = createMut.isPending || updateMut.isPending

  if (isEdit && loadingPrompt) {
    return (
      <div className="flex items-center justify-center py-20">
        <Loader2 className="h-8 w-8 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-4xl">
      {/* Header */}
      <div className="mb-6 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <button
            onClick={() => navigate('/admin/prompts')}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
          <div>
            <h1 className="text-xl font-bold text-gray-900">
              {isEdit ? 'Editar prompt' : 'Nuevo prompt'}
            </h1>
            <p className="text-sm text-gray-500">
              {isEdit ? 'Modifica los campos del prompt' : 'Configura los campos del nuevo prompt'}
            </p>
          </div>
        </div>
        <button
          onClick={handleSave}
          disabled={isSaving}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
        >
          {isSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
          {isEdit ? 'Actualizar' : 'Crear prompt'}
        </button>
      </div>

      {error && (
        <div className="mb-4 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-600">{error}</div>
      )}

      <div className="space-y-6">
        {/* Informacion basica */}
        <div className="rounded-lg border border-gray-200 bg-white p-6">
          <h2 className="mb-4 text-sm font-semibold text-gray-900 uppercase tracking-wide">Informacion basica</h2>
          <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
            <div>
              <label className="mb-1.5 block text-sm font-medium text-gray-700">Nombre del prompt *</label>
              <input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                className="w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                placeholder="Ej: Prompt Cobros SURA"
              />
            </div>
            <div>
              <label className="mb-1.5 block text-sm font-medium text-gray-700">Categoria</label>
              <select
                value={form.categoryId ?? ''}
                onChange={(e) => setForm({ ...form, categoryId: e.target.value || null })}
                className="w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              >
                <option value="">Sin categoria</option>
                {categories.map((c: any) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </div>
            <div className="md:col-span-2">
              <label className="mb-1.5 block text-sm font-medium text-gray-700">Descripcion</label>
              <input
                value={form.description ?? ''}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
                className="w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                placeholder="Breve descripcion del prompt"
              />
            </div>
          </div>
        </div>

        {/* System Prompt */}
        <div className="rounded-lg border border-gray-200 bg-white p-6">
          <h2 className="mb-4 text-sm font-semibold text-gray-900 uppercase tracking-wide">System Prompt</h2>
          <textarea
            value={form.systemPrompt ?? ''}
            onChange={(e) => setForm({ ...form, systemPrompt: e.target.value })}
            rows={10}
            className="w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            placeholder="Instrucciones del sistema para el agente..."
          />
        </div>

        {/* Result Prompt */}
        <div className="rounded-lg border border-gray-200 bg-white p-6">
          <h2 className="mb-4 text-sm font-semibold text-gray-900 uppercase tracking-wide">Result Prompt</h2>
          <textarea
            value={form.resultPrompt ?? ''}
            onChange={(e) => setForm({ ...form, resultPrompt: e.target.value })}
            rows={6}
            className="w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            placeholder='{"response_format":{"type":"json_object"},...}'
          />
        </div>

        {/* Analysis Prompts */}
        <div className="rounded-lg border border-gray-200 bg-white p-6">
          <h2 className="mb-4 text-sm font-semibold text-gray-900 uppercase tracking-wide">Analysis Prompts</h2>
          <textarea
            value={form.analysisPrompts ?? ''}
            onChange={(e) => setForm({ ...form, analysisPrompts: e.target.value })}
            rows={6}
            className="w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            placeholder="Prompts de analisis..."
          />
        </div>

        {/* Field Mapping */}
        <div className="rounded-lg border border-gray-200 bg-white p-6">
          <h2 className="mb-4 text-sm font-semibold text-gray-900 uppercase tracking-wide">Field Mapping</h2>
          <textarea
            value={form.fieldMapping ?? ''}
            onChange={(e) => setForm({ ...form, fieldMapping: e.target.value })}
            rows={4}
            className="w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            placeholder='{"Cliente":"CLIENTE","Identificacion":"IDENTIFICACION",...}'
          />
        </div>
      </div>
    </div>
  )
}
