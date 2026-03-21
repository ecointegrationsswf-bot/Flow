import { useState } from 'react'
import { Tag, Plus, Pencil, Trash2, Check, X, Loader2 } from 'lucide-react'
import {
  useAdminCategories,
  useCreateCategory,
  useUpdateCategory,
  useDeleteCategory,
} from '@/modules/admin/hooks/useAdminCategories'

export function CategoriesPage() {
  const { data: categories, isLoading } = useAdminCategories()
  const createMut = useCreateCategory()
  const updateMut = useUpdateCategory()
  const deleteMut = useDeleteCategory()

  const [newName, setNewName] = useState('')
  const [editId, setEditId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [error, setError] = useState<string | null>(null)

  const handleCreate = async () => {
    if (!newName.trim()) return
    setError(null)
    try {
      await createMut.mutateAsync(newName.trim())
      setNewName('')
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setError(axiosErr.response?.data?.error ?? 'Error al crear.')
    }
  }

  const handleUpdate = async (id: string) => {
    if (!editName.trim()) return
    setError(null)
    try {
      await updateMut.mutateAsync({ id, name: editName.trim() })
      setEditId(null)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setError(axiosErr.response?.data?.error ?? 'Error al actualizar.')
    }
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Eliminar esta categoria?')) return
    await deleteMut.mutateAsync(id)
  }

  const handleToggle = async (id: string, isActive: boolean) => {
    await updateMut.mutateAsync({ id, isActive: !isActive })
  }

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Categorias</h1>
        <p className="text-sm text-gray-500">Administra las categorias de agentes IA</p>
      </div>

      {error && (
        <div className="mb-4 rounded-md bg-red-50 p-3 text-sm text-red-600">{error}</div>
      )}

      {/* Create form */}
      <div className="mb-6 flex items-center gap-3">
        <input
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleCreate()}
          placeholder="Nueva categoria..."
          className="w-64 rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
        />
        <button
          onClick={handleCreate}
          disabled={createMut.isPending || !newName.trim()}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
        >
          {createMut.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          Agregar
        </button>
      </div>

      {/* List */}
      {isLoading ? (
        <div className="py-12 text-center text-gray-400">Cargando...</div>
      ) : !categories?.length ? (
        <div className="py-16 text-center">
          <Tag className="mx-auto h-12 w-12 text-gray-300" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">Sin categorias</h3>
        </div>
      ) : (
        <div className="rounded-lg bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Nombre</th>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Estado</th>
                <th className="px-6 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {categories.map((cat) => (
                <tr key={cat.id}>
                  <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-900">
                    {editId === cat.id ? (
                      <div className="flex items-center gap-2">
                        <input
                          value={editName}
                          onChange={(e) => setEditName(e.target.value)}
                          onKeyDown={(e) => e.key === 'Enter' && handleUpdate(cat.id)}
                          className="w-48 rounded border border-gray-300 px-2 py-1 text-sm focus:border-amber-500 focus:outline-none"
                          autoFocus
                        />
                        <button onClick={() => handleUpdate(cat.id)} className="text-green-600 hover:text-green-800">
                          <Check className="h-4 w-4" />
                        </button>
                        <button onClick={() => setEditId(null)} className="text-gray-400 hover:text-gray-600">
                          <X className="h-4 w-4" />
                        </button>
                      </div>
                    ) : (
                      cat.name
                    )}
                  </td>
                  <td className="whitespace-nowrap px-6 py-4">
                    <button
                      onClick={() => handleToggle(cat.id, cat.isActive)}
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        cat.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
                      }`}
                    >
                      {cat.isActive ? 'Activa' : 'Inactiva'}
                    </button>
                  </td>
                  <td className="whitespace-nowrap px-6 py-4 text-right">
                    <div className="flex items-center justify-end gap-1">
                      <button
                        onClick={() => { setEditId(cat.id); setEditName(cat.name) }}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                        title="Editar"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => handleDelete(cat.id)}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors"
                        title="Eliminar"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
