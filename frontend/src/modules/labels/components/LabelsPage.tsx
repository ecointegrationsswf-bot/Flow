import { useState } from 'react'
import { X, Trash2, Edit2, Plus, Tag } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { useLabels, useCreateLabel, useUpdateLabel, useDeleteLabel } from '@/shared/hooks/useLabels'
import type { ConversationLabel } from '@/shared/types'

const PRESET_COLORS = [
  '#10B981', '#EAB308', '#EF4444', '#3B82F6', '#8B5CF6',
  '#F97316', '#EC4899', '#06B6D4', '#14B8A6', '#6366F1',
]

export function LabelsPage() {
  const { data: labels, isLoading } = useLabels()
  const createLabel = useCreateLabel()
  const updateLabel = useUpdateLabel()
  const deleteLabel = useDeleteLabel()

  const [name, setName] = useState('')
  const [color, setColor] = useState('#10B981')
  const [keywordInput, setKeywordInput] = useState('')
  const [keywords, setKeywords] = useState<string[]>([])
  const [editingId, setEditingId] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ConversationLabel | null>(null)
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; msg: string } | null>(null)

  const resetForm = () => {
    setName('')
    setColor('#10B981')
    setKeywordInput('')
    setKeywords([])
    setEditingId(null)
  }

  const addKeyword = () => {
    const trimmed = keywordInput.trim()
    if (trimmed && !keywords.includes(trimmed)) {
      setKeywords([...keywords, trimmed])
      setKeywordInput('')
    }
  }

  const removeKeyword = (kw: string) => {
    setKeywords(keywords.filter((k) => k !== kw))
  }

  const handleKeywordKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault()
      addKeyword()
    }
  }

  const startEdit = (label: ConversationLabel) => {
    setEditingId(label.id)
    setName(label.name)
    setColor(label.color)
    setKeywords(label.keywords || [])
    setKeywordInput('')
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return

    try {
      if (editingId) {
        await updateLabel.mutateAsync({ id: editingId, name: name.trim(), color, keywords })
        setFeedback({ type: 'success', msg: 'Etiqueta actualizada correctamente' })
      } else {
        await createLabel.mutateAsync({ name: name.trim(), color, keywords })
        setFeedback({ type: 'success', msg: 'Etiqueta creada correctamente' })
      }
      resetForm()
    } catch {
      setFeedback({ type: 'error', msg: 'Error al guardar la etiqueta' })
    }

    setTimeout(() => setFeedback(null), 3000)
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    try {
      await deleteLabel.mutateAsync(deleteTarget.id)
      setFeedback({ type: 'success', msg: 'Etiqueta eliminada' })
    } catch {
      setFeedback({ type: 'error', msg: 'Error al eliminar la etiqueta' })
    }
    setDeleteTarget(null)
    setTimeout(() => setFeedback(null), 3000)
  }

  if (isLoading) return <LoadingSpinner />

  return (
    <div>
      <PageHeader
        title="Etiquetas"
        subtitle="Crea y gestiona etiquetas para clasificar el estado de las conversaciones"
      />

      {/* Feedback */}
      {feedback && (
        <div
          className={`mb-4 rounded-md px-4 py-3 text-sm font-medium ${
            feedback.type === 'success'
              ? 'bg-green-50 text-green-700 border border-green-200'
              : 'bg-red-50 text-red-700 border border-red-200'
          }`}
        >
          {feedback.msg}
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Form */}
        <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
          <h2 className="mb-4 text-lg font-semibold text-gray-900">
            {editingId ? 'Editar etiqueta' : 'Nueva etiqueta'}
          </h2>
          <hr className="mb-5 border-gray-200" />

          <form onSubmit={handleSubmit} className="space-y-5">
            {/* Name + Color */}
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="mb-1.5 block text-sm font-medium text-gray-700">Nombre</label>
                <input
                  type="text"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Ej: Confirmo pago"
                  className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  required
                />
              </div>
              <div>
                <label className="mb-1.5 block text-sm font-medium text-gray-700">Color Etiqueta</label>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={color}
                    onChange={(e) => setColor(e.target.value)}
                    className="h-10 w-14 cursor-pointer rounded border border-gray-300"
                  />
                  <div className="flex flex-wrap gap-1">
                    {PRESET_COLORS.map((c) => (
                      <button
                        key={c}
                        type="button"
                        onClick={() => setColor(c)}
                        className={`h-6 w-6 rounded-md border-2 transition-transform hover:scale-110 ${
                          color === c ? 'border-gray-800 scale-110' : 'border-transparent'
                        }`}
                        style={{ backgroundColor: c }}
                      />
                    ))}
                  </div>
                </div>
              </div>
            </div>

            {/* Keywords */}
            <div>
              <label className="mb-1.5 block text-sm font-medium text-gray-700">Palabras Clave</label>
              <div className="flex gap-2">
                <input
                  type="text"
                  value={keywordInput}
                  onChange={(e) => setKeywordInput(e.target.value)}
                  onKeyDown={handleKeywordKeyDown}
                  placeholder="Escribe y presiona Enter o Agregar"
                  className="flex-1 rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
                <button
                  type="button"
                  onClick={addKeyword}
                  className="rounded-md bg-gray-500 px-4 py-2 text-sm font-medium text-white hover:bg-gray-600 transition-colors"
                >
                  Agregar
                </button>
              </div>

              {/* Keywords chips */}
              {keywords.length > 0 && (
                <div className="mt-3 flex flex-wrap gap-2">
                  {keywords.map((kw) => (
                    <span
                      key={kw}
                      className="inline-flex items-center gap-1 rounded-full bg-blue-50 px-3 py-1 text-sm text-blue-700"
                    >
                      {kw}
                      <button
                        type="button"
                        onClick={() => removeKeyword(kw)}
                        className="rounded-full p-0.5 hover:bg-blue-200 transition-colors"
                      >
                        <X className="h-3 w-3" />
                      </button>
                    </span>
                  ))}
                </div>
              )}
            </div>

            {/* Buttons */}
            <div className="flex gap-2">
              <button
                type="submit"
                disabled={createLabel.isPending || updateLabel.isPending}
                className="flex-1 rounded-md bg-blue-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
              >
                {createLabel.isPending || updateLabel.isPending
                  ? 'Guardando...'
                  : editingId
                  ? 'Actualizar etiqueta'
                  : 'Crear etiqueta'}
              </button>
              {editingId && (
                <button
                  type="button"
                  onClick={resetForm}
                  className="rounded-md border border-gray-300 px-4 py-2.5 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
                >
                  Cancelar
                </button>
              )}
            </div>
          </form>
        </div>

        {/* List */}
        <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
          <h2 className="mb-4 text-lg font-semibold text-gray-900">Etiquetas</h2>
          <hr className="mb-4 border-gray-200" />

          {!labels || labels.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-gray-400">
              <Tag className="mb-3 h-12 w-12" />
              <p className="text-sm">No hay etiquetas creadas</p>
              <p className="text-xs mt-1">Crea tu primera etiqueta usando el formulario</p>
            </div>
          ) : (
            <div className="space-y-2">
              {labels.map((label) => (
                <div
                  key={label.id}
                  className="flex items-center justify-between rounded-lg border border-gray-100 bg-gray-50 px-4 py-3 hover:bg-gray-100 transition-colors group"
                >
                  <div className="flex items-center gap-3">
                    <div
                      className="h-7 w-7 rounded-md shrink-0"
                      style={{ backgroundColor: label.color }}
                    />
                    <div>
                      <span className="text-sm font-medium text-gray-900">{label.name}</span>
                      {label.keywords && label.keywords.length > 0 && (
                        <div className="flex flex-wrap gap-1 mt-1">
                          {label.keywords.map((kw) => (
                            <span key={kw} className="rounded bg-white px-1.5 py-0.5 text-xs text-gray-500 border border-gray-200">
                              {kw}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>

                  <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                    <button
                      onClick={() => startEdit(label)}
                      className="rounded p-1.5 text-gray-400 hover:bg-blue-50 hover:text-blue-600 transition-colors"
                      title="Editar"
                    >
                      <Edit2 className="h-4 w-4" />
                    </button>
                    <button
                      onClick={() => setDeleteTarget(label)}
                      className="rounded p-1.5 text-gray-400 hover:bg-red-50 hover:text-red-600 transition-colors"
                      title="Eliminar"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Confirm delete */}
      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title="Eliminar etiqueta"
        description={`¿Estás seguro de eliminar la etiqueta "${deleteTarget?.name}"? Esta acción no se puede deshacer.`}
        confirmLabel="Eliminar"
        variant="danger"
      />
    </div>
  )
}
