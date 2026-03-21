import { useState, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { Bot, Plus, Pencil, Trash2, ArrowRightToLine, ChevronDown, ChevronRight } from 'lucide-react'
import {
  useAdminAgentTemplates,
  useDeleteAgentTemplate,
  type AgentTemplate,
} from '@/modules/admin/hooks/useAdminAgentTemplates'
import { MigrateTemplateModal } from './MigrateTemplateModal'

export function AgentTemplatesPage() {
  const navigate = useNavigate()
  const { data: templates, isLoading } = useAdminAgentTemplates()
  const deleteMut = useDeleteAgentTemplate()

  const [migrateTemplate, setMigrateTemplate] = useState<AgentTemplate | null>(null)
  const [collapsedCategories, setCollapsedCategories] = useState<Set<string>>(new Set())

  const grouped = useMemo(() => {
    if (!templates) return {}
    return templates.reduce<Record<string, AgentTemplate[]>>((acc, t) => {
      const cat = t.category || 'Sin categoria'
      if (!acc[cat]) acc[cat] = []
      acc[cat].push(t)
      return acc
    }, {})
  }, [templates])

  const categories = useMemo(() => Object.keys(grouped).sort(), [grouped])

  const toggleCategory = (cat: string) => {
    setCollapsedCategories((prev) => {
      const next = new Set(prev)
      if (next.has(cat)) next.delete(cat)
      else next.add(cat)
      return next
    })
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Eliminar esta plantilla?')) return
    await deleteMut.mutateAsync(id)
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Agentes</h1>
          <p className="text-sm text-gray-500">Plantillas globales de agentes IA</p>
        </div>
        <button
          onClick={() => navigate('/admin/agent-templates/new')}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Nueva plantilla
        </button>
      </div>

      {isLoading ? (
        <div className="py-12 text-center text-gray-400">Cargando...</div>
      ) : categories.length === 0 ? (
        <div className="py-16 text-center">
          <Bot className="mx-auto h-12 w-12 text-gray-300" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">Sin plantillas</h3>
          <p className="mt-1 text-sm text-gray-500">Crea tu primera plantilla de agente IA.</p>
        </div>
      ) : (
        <div className="space-y-4">
          {categories.map((cat) => {
            const items = grouped[cat]
            const isCollapsed = collapsedCategories.has(cat)
            return (
              <div key={cat} className="rounded-lg bg-white shadow-sm">
                <button
                  onClick={() => toggleCategory(cat)}
                  className="flex w-full items-center justify-between px-5 py-3 text-left hover:bg-gray-50"
                >
                  <div className="flex items-center gap-3">
                    {isCollapsed ? (
                      <ChevronRight className="h-4 w-4 text-gray-400" />
                    ) : (
                      <ChevronDown className="h-4 w-4 text-gray-400" />
                    )}
                    <span className="text-sm font-semibold text-gray-900">{cat}</span>
                    <span className="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">
                      {items.length}
                    </span>
                  </div>
                </button>

                {!isCollapsed && (
                  <div className="divide-y divide-gray-100 border-t border-gray-100">
                    {items.map((t) => (
                      <div key={t.id} className="flex items-center justify-between px-5 py-3">
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <p className="text-sm font-medium text-gray-900">{t.name}</p>
                            <span
                              className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                                t.isActive
                                  ? 'bg-green-100 text-green-700'
                                  : 'bg-red-100 text-red-700'
                              }`}
                            >
                              {t.isActive ? 'Activo' : 'Inactivo'}
                            </span>
                          </div>
                          <p className="mt-0.5 truncate text-xs text-gray-500">
                            {t.systemPrompt.slice(0, 120)}
                            {t.systemPrompt.length > 120 && '...'}
                          </p>
                        </div>
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => setMigrateTemplate(t)}
                            title="Migrar a tenant"
                            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                          >
                            <ArrowRightToLine className="h-4 w-4" />
                          </button>
                          <button
                            onClick={() => navigate(`/admin/agent-templates/${t.id}/edit`)}
                            title="Editar"
                            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button
                            onClick={() => handleDelete(t.id)}
                            title="Eliminar"
                            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}

      {migrateTemplate && (
        <MigrateTemplateModal
          template={migrateTemplate}
          onClose={() => setMigrateTemplate(null)}
        />
      )}
    </div>
  )
}
