import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { UserPlus, Users, Trash2, Pencil, Loader2, AlertCircle } from 'lucide-react'
import { Badge } from '@/shared/components/Badge'
import { EmptyState } from '@/shared/components/EmptyState'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { useUsers, useCreateUser, useUpdateUser, useDeleteUser } from '@/shared/hooks/useUsers'
import { useAgents } from '@/shared/hooks/useAgents'
import type { AppUser } from '@/shared/types'

const userSchema = z.object({
  fullName: z.string().min(2, 'Nombre requerido'),
  email: z.string().email('Email invalido'),
  role: z.string().min(1, 'Rol requerido'),
  password: z.string().min(6, 'Minimo 6 caracteres'),
  canEditPhone: z.boolean(),
})

const editSchema = z.object({
  fullName: z.string().min(2, 'Nombre requerido'),
  email: z.string().email('Email invalido'),
  role: z.string().min(1, 'Rol requerido'),
  isActive: z.boolean(),
  canEditPhone: z.boolean(),
  password: z.string().optional(),
})

type UserForm = z.infer<typeof userSchema>
type EditForm = z.infer<typeof editSchema>

export function UsersTab() {
  const { data: users, isLoading } = useUsers()
  const { data: agents } = useAgents()
  const createMutation = useCreateUser()
  const updateMutation = useUpdateUser()
  const deleteMutation = useDeleteUser()

  const [showForm, setShowForm] = useState(false)
  const [editUser, setEditUser] = useState<AppUser | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<AppUser | null>(null)

  // Estado para agentes seleccionados (fuera de react-hook-form porque es multi-select)
  const [selectedAgentIds, setSelectedAgentIds] = useState<string[]>([])
  const [editSelectedAgentIds, setEditSelectedAgentIds] = useState<string[]>([])

  // Create form
  const createForm = useForm<UserForm>({
    resolver: zodResolver(userSchema),
    defaultValues: { role: 'Cobros', canEditPhone: false },
  })

  // Edit form
  const editForm = useForm<EditForm>({
    resolver: zodResolver(editSchema),
  })

  const toggleAgentId = (agentId: string, list: string[], setList: (v: string[]) => void) => {
    setList(list.includes(agentId) ? list.filter(id => id !== agentId) : [...list, agentId])
  }

  const onCreateSubmit = async (data: UserForm) => {
    try {
      await createMutation.mutateAsync({ ...data, allowedAgentIds: selectedAgentIds })
      createForm.reset({ fullName: '', email: '', password: '', role: 'Cobros', canEditPhone: false })
      setSelectedAgentIds([])
      setShowForm(false)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      createForm.setError('email', { message: axiosErr.response?.data?.error ?? 'Error al crear usuario' })
    }
  }

  const openEdit = (user: AppUser) => {
    setEditUser(user)
    setEditSelectedAgentIds(user.allowedAgentIds ?? [])
    editForm.reset({
      fullName: user.fullName,
      email: user.email,
      role: user.role,
      isActive: user.isActive,
      canEditPhone: user.canEditPhone,
      password: '',
    })
  }

  const onEditSubmit = async (data: EditForm) => {
    if (!editUser) return
    try {
      await updateMutation.mutateAsync({
        id: editUser.id,
        fullName: data.fullName,
        email: data.email,
        role: data.role,
        isActive: data.isActive,
        canEditPhone: data.canEditPhone,
        allowedAgentIds: editSelectedAgentIds,
        password: data.password || undefined,
      })
      setEditUser(null)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      editForm.setError('email', { message: axiosErr.response?.data?.error ?? 'Error al actualizar' })
    }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    await deleteMutation.mutateAsync(deleteTarget.id)
    setDeleteTarget(null)
  }

  const AgentSelector = ({ selected, onChange }: { selected: string[]; onChange: (ids: string[]) => void }) => (
    <div className="mt-3">
      <label className="block text-xs font-medium text-gray-600 mb-2">
        Agentes que puede monitorear {selected.length === 0 && <span className="text-gray-400">(todos si no se selecciona ninguno)</span>}
      </label>
      <div className="flex flex-wrap gap-2">
        {agents?.map((agent) => {
          const isSelected = selected.includes(agent.id)
          return (
            <button
              key={agent.id}
              type="button"
              onClick={() => toggleAgentId(agent.id, selected, onChange)}
              className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                isSelected
                  ? 'border-blue-500 bg-blue-50 text-blue-700'
                  : 'border-gray-300 bg-white text-gray-600 hover:border-gray-400'
              }`}
            >
              {agent.name} ({agent.type})
            </button>
          )
        })}
        {(!agents || agents.length === 0) && (
          <p className="text-xs text-gray-400">No hay agentes configurados</p>
        )}
      </div>
    </div>
  )

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-gray-900">
          Usuarios {users ? `(${users.length})` : ''}
        </h3>
        <button
          onClick={() => { setShowForm(!showForm); setEditUser(null); setSelectedAgentIds([]) }}
          className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
        >
          <UserPlus className="h-3.5 w-3.5" /> Nuevo usuario
        </button>
      </div>

      {/* Create form */}
      {showForm && (
        <form onSubmit={createForm.handleSubmit(onCreateSubmit)} className="mb-4 rounded-lg border border-blue-200 bg-blue-50 p-4">
          <h4 className="mb-3 text-sm font-medium text-gray-900">Crear usuario</h4>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <input
                placeholder="Nombre completo"
                {...createForm.register('fullName')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {createForm.formState.errors.fullName && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.fullName.message}</p>
              )}
            </div>
            <div>
              <input
                placeholder="Email"
                type="email"
                {...createForm.register('email')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {createForm.formState.errors.email && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.email.message}</p>
              )}
            </div>
            <select {...createForm.register('role')} className="rounded-md border border-gray-300 px-3 py-2 text-sm">
              <option value="Cobros">Cobros</option>
              <option value="Supervisor">Supervisor</option>
              <option value="Admin">Admin</option>
              <option value="ReadOnly">Solo lectura</option>
            </select>
            <div>
              <input
                placeholder="Contrasena"
                type="password"
                {...createForm.register('password')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {createForm.formState.errors.password && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.password.message}</p>
              )}
            </div>
          </div>
          <div className="mt-3 flex items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" {...createForm.register('canEditPhone')} className="rounded border-gray-300" />
              Puede editar numero de telefono
            </label>
          </div>
          <AgentSelector selected={selectedAgentIds} onChange={setSelectedAgentIds} />
          <div className="mt-3 flex items-center gap-2">
            <button
              type="submit"
              disabled={createMutation.isPending}
              className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {createMutation.isPending && <Loader2 className="h-3 w-3 animate-spin" />}
              Guardar
            </button>
            <button
              type="button"
              onClick={() => setShowForm(false)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancelar
            </button>
          </div>
        </form>
      )}

      {/* Edit form */}
      {editUser && (
        <form onSubmit={editForm.handleSubmit(onEditSubmit)} className="mb-4 rounded-lg border border-amber-200 bg-amber-50 p-4">
          <h4 className="mb-3 text-sm font-medium text-gray-900">Editar usuario: {editUser.fullName}</h4>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <input
                placeholder="Nombre completo"
                {...editForm.register('fullName')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {editForm.formState.errors.fullName && (
                <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.fullName.message}</p>
              )}
            </div>
            <div>
              <input
                placeholder="Email"
                type="email"
                {...editForm.register('email')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {editForm.formState.errors.email && (
                <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.email.message}</p>
              )}
            </div>
            <select {...editForm.register('role')} className="rounded-md border border-gray-300 px-3 py-2 text-sm">
              <option value="Cobros">Cobros</option>
              <option value="Supervisor">Supervisor</option>
              <option value="Admin">Admin</option>
              <option value="ReadOnly">Solo lectura</option>
            </select>
            <input
              placeholder="Nueva contrasena (dejar vacio para no cambiar)"
              type="password"
              {...editForm.register('password')}
              className="rounded-md border border-gray-300 px-3 py-2 text-sm"
            />
          </div>
          <div className="mt-3 flex items-center gap-4">
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" {...editForm.register('isActive')} className="rounded border-gray-300" />
              Activo
            </label>
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" {...editForm.register('canEditPhone')} className="rounded border-gray-300" />
              Puede editar numero de telefono
            </label>
          </div>
          <AgentSelector selected={editSelectedAgentIds} onChange={setEditSelectedAgentIds} />
          <div className="mt-3 flex items-center gap-2">
            <button
              type="submit"
              disabled={updateMutation.isPending}
              className="flex items-center gap-1.5 rounded-md bg-amber-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-700 disabled:opacity-50"
            >
              {updateMutation.isPending && <Loader2 className="h-3 w-3 animate-spin" />}
              Actualizar
            </button>
            <button
              type="button"
              onClick={() => setEditUser(null)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancelar
            </button>
          </div>
        </form>
      )}

      {/* Error messages */}
      {createMutation.isError && (
        <div className="mb-3 flex items-center gap-1.5 text-xs text-red-600">
          <AlertCircle className="h-3.5 w-3.5" />
          <span>Error al crear el usuario.</span>
        </div>
      )}

      {/* Users list */}
      {isLoading ? (
        <div className="flex items-center justify-center py-8">
          <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
        </div>
      ) : users && users.length > 0 ? (
        <div className="overflow-hidden rounded-lg bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Nombre</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Email</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Rol</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Estado</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {users.map((u) => (
                <tr key={u.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">{u.fullName}</td>
                  <td className="px-4 py-3 text-sm text-gray-500">{u.email}</td>
                  <td className="px-4 py-3"><Badge variant={u.role}>{u.role}</Badge></td>
                  <td className="px-4 py-3">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${u.isActive ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                      {u.isActive ? 'Activo' : 'Inactivo'}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1">
                      <button
                        onClick={() => { openEdit(u); setShowForm(false) }}
                        className="rounded p-1.5 text-gray-400 hover:bg-blue-50 hover:text-blue-600"
                        title="Editar"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => setDeleteTarget(u)}
                        className="rounded p-1.5 text-gray-400 hover:bg-red-50 hover:text-red-600"
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
      ) : (
        <EmptyState icon={Users} title="Sin usuarios" description="Agrega usuarios para gestionar el equipo" />
      )}

      {/* Confirm delete */}
      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title="Eliminar usuario"
        description={`Se eliminara permanentemente a "${deleteTarget?.fullName}". Esta accion no se puede deshacer.`}
        confirmLabel="Eliminar"
        variant="danger"
      />
    </div>
  )
}
