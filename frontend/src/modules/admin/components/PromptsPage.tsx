import { useNavigate } from 'react-router-dom'
import { Plus, Pencil, Trash2, ToggleLeft, ToggleRight, Loader2, FileText, ChevronDown, ChevronUp } from 'lucide-react'
import { useState } from 'react'
import {
  useAdminPrompts,
  useTogglePrompt,
  useDeletePrompt,
} from '../hooks/useAdminPrompts'

export function PromptsPage() {
  const navigate = useNavigate()
  const { data: prompts = [], isLoading } = useAdminPrompts()
  const toggleMut = useTogglePrompt()
  const deleteMut = useDeletePrompt()
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const handleDelete = async (id: string) => {
    if (!confirm('Eliminar este prompt?')) return
    await deleteMut.mutateAsync(id)
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-20">
        <Loader2 className="h-8 w-8 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Prompts</h1>
          <p className="text-sm text-gray-500">Administra los prompts de los agentes</p>
        </div>
        <button
          onClick={() => navigate('/admin/prompts/new')}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Nuevo prompt
        </button>
      </div>

      {prompts.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 bg-white p-12 text-center">
          <FileText className="mx-auto h-12 w-12 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">No hay prompts creados</p>
          <button onClick={() => navigate('/admin/prompts/new')} className="mt-3 text-sm font-medium text-blue-600 hover:text-blue-700">
            Crear primer prompt
          </button>
        </div>
      ) : (
        <div className="space-y-3">
          {prompts.map((p) => (
            <div key={p.id} className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
              <div className="flex items-center justify-between px-5 py-4">
                <div className="flex items-center gap-4 min-w-0 flex-1">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-indigo-100">
                    <FileText className="h-5 w-5 text-indigo-600" />
                  </div>
                  <div className="min-w-0">
                    <p className="font-semibold text-gray-900">{p.name}</p>
                    <div className="flex items-center gap-2 mt-0.5">
                      {p.description && <span className="text-xs text-gray-500 truncate max-w-xs">{p.description}</span>}
                      {p.categoryName && (
                        <span className="inline-block rounded-full bg-blue-50 px-2 py-0.5 text-[10px] font-medium text-blue-600">
                          {p.categoryName}
                        </span>
                      )}
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 shrink-0">
                  <button onClick={() => toggleMut.mutate(p.id)} title={p.isActive ? 'Desactivar' : 'Activar'}>
                    {p.isActive ? <ToggleRight className="h-6 w-6 text-green-500" /> : <ToggleLeft className="h-6 w-6 text-gray-300" />}
                  </button>
                  <button
                    onClick={() => setExpandedId(expandedId === p.id ? null : p.id)}
                    className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                  >
                    {expandedId === p.id ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                  </button>
                  <button
                    onClick={() => navigate(`/admin/prompts/${p.id}/edit`)}
                    className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(p.id)}
                    className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>

              {expandedId === p.id && (
                <div className="border-t border-gray-100 bg-gray-50 px-5 py-4 space-y-4">
                  {p.systemPrompt && (
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase tracking-wide">System Prompt</label>
                      <pre className="mt-1 whitespace-pre-wrap rounded-lg border border-gray-200 bg-white p-3 text-xs text-gray-700 max-h-40 overflow-y-auto">{p.systemPrompt}</pre>
                    </div>
                  )}
                  {p.resultPrompt && (
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Result Prompt</label>
                      <pre className="mt-1 whitespace-pre-wrap rounded-lg border border-gray-200 bg-white p-3 text-xs text-gray-700 max-h-40 overflow-y-auto">{p.resultPrompt}</pre>
                    </div>
                  )}
                  {p.analysisPrompts && (
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Analysis Prompts</label>
                      <pre className="mt-1 whitespace-pre-wrap rounded-lg border border-gray-200 bg-white p-3 text-xs text-gray-700 max-h-40 overflow-y-auto">{p.analysisPrompts}</pre>
                    </div>
                  )}
                  {p.fieldMapping && (
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Field Mapping</label>
                      <pre className="mt-1 whitespace-pre-wrap rounded-lg border border-gray-200 bg-white p-3 text-xs text-gray-700 max-h-40 overflow-y-auto">{p.fieldMapping}</pre>
                    </div>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
