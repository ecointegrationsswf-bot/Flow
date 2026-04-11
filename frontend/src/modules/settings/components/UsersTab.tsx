import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import {
  UserPlus, Users, Trash2, Pencil, Loader2, AlertCircle,
  ShieldCheck, X, Check, Shield,
} from 'lucide-react'
import { Badge } from '@/shared/components/Badge'
import { EmptyState } from '@/shared/components/EmptyState'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { useUsers, useCreateUser, useUpdateUser, useDeleteUser } from '@/shared/hooks/useUsers'
import { useAgents } from '@/shared/hooks/useAgents'
import type { AppUser } from '@/shared/types'

// ─── Permisos disponibles ──────────────────────────────────────────────────────
interface PermissionDef {
  id: string
  label: string
  description: string
  group: string
}

const PERMISSIONS: PermissionDef[] = [
  { id: 'view_monitor',            label: 'Ver monitor',              description: 'Ver conversaciones en tiempo real',          group: 'Monitor' },
  { id: 'take_conversation',       label: 'Tomar conversaciones',     description: 'Pausar IA y atender manualmente',            group: 'Monitor' },
  { id: 'view_campaigns',          label: 'Ver campañas',             description: 'Consultar el listado de campañas',           group: 'Campañas' },
  { id: 'create_campaigns',        label: 'Crear campañas',           description: 'Crear y lanzar campañas',                   group: 'Campañas' },
  { id: 'upload_contacts',         label: 'Subir contactos',          description: 'Cargar archivos de contactos a campañas',    group: 'Campañas' },
  { id: 'view_campaign_templates', label: 'Ver maestros de campaña',  description: 'Consultar maestros de campaña',             group: 'Maestros' },
  { id: 'edit_campaign_templates', label: 'Editar maestros',          description: 'Crear y editar maestros de campaña',        group: 'Maestros' },
  { id: 'view_agents',             label: 'Ver agentes',              description: 'Ver la configuración de los agentes IA',    group: 'Agentes' },
  { id: 'edit_agents',             label: 'Editar agentes',           description: 'Crear y modificar agentes IA',              group: 'Agentes' },
  { id: 'view_whatsapp_lines',     label: 'Ver líneas de WhatsApp',   description: 'Ver las líneas de WhatsApp configuradas',   group: 'Configuración' },
  { id: 'create_users',            label: 'Gestionar usuarios',       description: 'Crear, editar y eliminar usuarios',         group: 'Configuración' },
  { id: 'view_reports',            label: 'Ver reportes',             description: 'Acceder al dashboard y estadísticas',       group: 'Configuración' },
]

const PERMISSION_GROUPS = [...new Set(PERMISSIONS.map(p => p.group))]

// ─── Tabla de permisos reutilizable ───────────────────────────────────────────
function PermissionsTable({ selected, onChange }: { selected: string[]; onChange: (p: string[]) => void }) {
  const toggle = (id: string) =>
    onChange(selected.includes(id) ? selected.filter(p => p !== id) : [...selected, id])

  const toggleGroup = (group: string) => {
    const ids = PERMISSIONS.filter(p => p.group === group).map(p => p.id)
    const allOn = ids.every(id => selected.includes(id))
    onChange(allOn ? selected.filter(id => !ids.includes(id)) : [...new Set([...selected, ...ids])])
  }

  return (
    <div className="mt-4">
      <div className="mb-2 flex items-center gap-2">
        <ShieldCheck className="h-4 w-4 text-blue-600" />
        <span className="text-xs font-semibold text-gray-700 uppercase tracking-wide">Permisos de acceso</span>
        <span className="ml-auto text-xs text-gray-400">{selected.length} activos</span>
      </div>
      <div className="rounded-lg border border-gray-200 bg-white overflow-hidden">
        {PERMISSION_GROUPS.map((group, gi) => {
          const perms = PERMISSIONS.filter(p => p.group === group)
          const allOn = perms.every(p => selected.includes(p.id))
          const someOn = perms.some(p => selected.includes(p.id))
          return (
            <div key={group} className={gi > 0 ? 'border-t border-gray-100' : ''}>
              <div
                className="flex items-center gap-3 bg-gray-50 px-4 py-2 cursor-pointer hover:bg-gray-100 transition-colors"
                onClick={() => toggleGroup(group)}
              >
                <input
                  type="checkbox" checked={allOn} readOnly
                  ref={el => { if (el) el.indeterminate = someOn && !allOn }}
                  className="h-3.5 w-3.5 rounded border-gray-300 text-blue-600 pointer-events-none"
                />
                <span className="text-xs font-semibold text-gray-600">{group}</span>
              </div>
              {perms.map(perm => (
                <label key={perm.id} className="flex items-center gap-3 px-5 py-2.5 hover:bg-blue-50/50 cursor-pointer transition-colors border-t border-gray-50">
                  <input
                    type="checkbox" checked={selected.includes(perm.id)}
                    onChange={() => toggle(perm.id)}
                    className="h-3.5 w-3.5 rounded border-gray-300 text-blue-600"
                  />
                  <div className="flex-1 min-w-0">
                    <span className="text-sm font-medium text-gray-800">{perm.label}</span>
                    <p className="text-xs text-gray-400">{perm.description}</p>
                  </div>
                  {selected.includes(perm.id) && (
                    <span className="shrink-0 rounded-full bg-blue-100 px-2 py-0.5 text-[10px] font-medium text-blue-600">Activo</span>
                  )}
                </label>
              ))}
            </div>
          )
        })}
      </div>
    </div>
  )
}

// ─── Modal de permisos ─────────────────────────────────────────────────────────
function PermissionsModal({
  user,
  onClose,
}: {
  user: AppUser
  onClose: () => void
}) {
  const updateMut = useUpdateUser()
  const [perms, setPerms] = useState<string[]>(user.permissions ?? [])
  const [saved, setSaved] = useState(false)

  const handleSave = async () => {
    await updateMut.mutateAsync({
      id: user.id,
      fullName: user.fullName,
      email: user.email,
      role: user.role,
      isActive: user.isActive,
      canEditPhone: user.canEditPhone,
      allowedAgentIds: user.allowedAgentIds ?? [],
      permissions: perms,
    })
    setSaved(true)
    setTimeout(() => { setSaved(false); onClose() }, 800)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="w-full max-w-lg rounded-xl bg-white shadow-2xl flex flex-col max-h-[90vh]">

        {/* Header */}
        <div className="flex items-center gap-3 border-b border-gray-200 px-6 py-4 shrink-0">
          <div className="flex h-9 w-9 items-center justify-center rounded-full bg-blue-100">
            <Shield className="h-4 w-4 text-blue-600" />
          </div>
          <div className="flex-1 min-w-0">
            <h2 className="text-base font-semibold text-gray-900 truncate">Permisos de {user.fullName}</h2>
            <p className="text-xs text-gray-500">{user.email} · {user.role}</p>
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 transition-colors">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Resumen rápido */}
        <div className="px-6 pt-4 shrink-0">
          <div className="flex flex-wrap gap-2">
            {perms.length === 0 ? (
              <span className="text-xs text-gray-400 italic">Sin permisos asignados</span>
            ) : (
              perms.map(pid => {
                const def = PERMISSIONS.find(p => p.id === pid)
                return def ? (
                  <span
                    key={pid}
                    onClick={() => setPerms(perms.filter(p => p !== pid))}
                    className="inline-flex items-center gap-1 rounded-full bg-blue-50 border border-blue-200 px-2.5 py-0.5 text-xs font-medium text-blue-700 cursor-pointer hover:bg-red-50 hover:border-red-200 hover:text-red-600 transition-colors group"
                    title="Clic para quitar"
                  >
                    {def.label}
                    <X className="h-2.5 w-2.5 opacity-0 group-hover:opacity-100 transition-opacity" />
                  </span>
                ) : null
              })
            )}
          </div>
          <p className="mt-2 text-[11px] text-gray-400">
            {perms.length > 0 ? 'Haz clic en un permiso para quitarlo rápidamente.' : ''}
          </p>
        </div>

        {/* Tabla de permisos */}
        <div className="overflow-y-auto px-6 pb-4 flex-1">
          <PermissionsTable selected={perms} onChange={setPerms} />
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-gray-200 px-6 py-4 shrink-0">
          <span className="text-xs text-gray-400">
            {perms.length} de {PERMISSIONS.length} permisos activos
          </span>
          <div className="flex gap-3">
            <button
              onClick={onClose}
              className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
            >
              Cancelar
            </button>
            <button
              onClick={handleSave}
              disabled={updateMut.isPending || saved}
              className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-60 transition-colors"
            >
              {saved ? (
                <><Check className="h-4 w-4" /> Guardado</>
              ) : updateMut.isPending ? (
                <><Loader2 className="h-4 w-4 animate-spin" /> Guardando...</>
              ) : (
                <><ShieldCheck className="h-4 w-4" /> Guardar permisos</>
              )}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

// ─── Esquemas de formulario ────────────────────────────────────────────────────
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

// ─── Componente principal ──────────────────────────────────────────────────────
export function UsersTab() {
  const { data: users, isLoading } = useUsers()
  const { data: agents } = useAgents()
  const createMutation = useCreateUser()
  const updateMutation = useUpdateUser()
  const deleteMutation = useDeleteUser()

  const [showForm, setShowForm] = useState(false)
  const [editUser, setEditUser] = useState<AppUser | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<AppUser | null>(null)
  const [permissionsUser, setPermissionsUser] = useState<AppUser | null>(null)

  const [selectedAgentIds, setSelectedAgentIds] = useState<string[]>([])
  const [editSelectedAgentIds, setEditSelectedAgentIds] = useState<string[]>([])
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>([])
  const [editSelectedPermissions, setEditSelectedPermissions] = useState<string[]>([])

  const createForm = useForm<UserForm>({
    resolver: zodResolver(userSchema),
    defaultValues: { role: 'Cobros', canEditPhone: false },
  })

  const editForm = useForm<EditForm>({ resolver: zodResolver(editSchema) })

  const toggleAgentId = (agentId: string, list: string[], setList: (v: string[]) => void) => {
    setList(list.includes(agentId) ? list.filter(id => id !== agentId) : [...list, agentId])
  }

  const onCreateSubmit = async (data: UserForm) => {
    try {
      await createMutation.mutateAsync({ ...data, allowedAgentIds: selectedAgentIds, permissions: selectedPermissions })
      createForm.reset({ fullName: '', email: '', password: '', role: 'Cobros', canEditPhone: false })
      setSelectedAgentIds([])
      setSelectedPermissions([])
      setShowForm(false)
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string } } }
      createForm.setError('email', { message: e.response?.data?.error ?? 'Error al crear usuario' })
    }
  }

  const openEdit = (user: AppUser) => {
    setEditUser(user)
    setEditSelectedAgentIds(user.allowedAgentIds ?? [])
    setEditSelectedPermissions(user.permissions ?? [])
    editForm.reset({ fullName: user.fullName, email: user.email, role: user.role, isActive: user.isActive, canEditPhone: user.canEditPhone, password: '' })
  }

  const onEditSubmit = async (data: EditForm) => {
    if (!editUser) return
    try {
      await updateMutation.mutateAsync({
        id: editUser.id, fullName: data.fullName, email: data.email, role: data.role,
        isActive: data.isActive, canEditPhone: data.canEditPhone,
        allowedAgentIds: editSelectedAgentIds, permissions: editSelectedPermissions,
        password: data.password || undefined,
      })
      setEditUser(null)
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string } } }
      editForm.setError('email', { message: e.response?.data?.error ?? 'Error al actualizar' })
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
        {agents?.map(agent => {
          const isSel = selected.includes(agent.id)
          return (
            <button key={agent.id} type="button" onClick={() => toggleAgentId(agent.id, selected, onChange)}
              className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${isSel ? 'border-blue-500 bg-blue-50 text-blue-700' : 'border-gray-300 bg-white text-gray-600 hover:border-gray-400'}`}>
              {agent.name} ({agent.type})
            </button>
          )
        })}
        {(!agents || agents.length === 0) && <p className="text-xs text-gray-400">No hay agentes configurados</p>}
      </div>
    </div>
  )

  return (
    <div>
      {/* Header */}
      <div className="mb-4 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-gray-900">Usuarios {users ? `(${users.length})` : ''}</h3>
        <button
          onClick={() => { setShowForm(!showForm); setEditUser(null); setSelectedAgentIds([]); setSelectedPermissions([]) }}
          className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
        >
          <UserPlus className="h-3.5 w-3.5" /> Nuevo usuario
        </button>
      </div>

      {/* ── Crear usuario ── */}
      {showForm && (
        <form onSubmit={createForm.handleSubmit(onCreateSubmit)} className="mb-4 rounded-lg border border-blue-200 bg-blue-50 p-4">
          <h4 className="mb-3 text-sm font-medium text-gray-900">Crear usuario</h4>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <input placeholder="Nombre completo" {...createForm.register('fullName')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
              {createForm.formState.errors.fullName && <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.fullName.message}</p>}
            </div>
            <div>
              <input placeholder="Email" type="email" {...createForm.register('email')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
              {createForm.formState.errors.email && <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.email.message}</p>}
            </div>
            <select {...createForm.register('role')} className="rounded-md border border-gray-300 px-3 py-2 text-sm">
              <option value="Cobros">Cobros</option>
              <option value="Supervisor">Supervisor</option>
              <option value="Admin">Admin</option>
              <option value="ReadOnly">Solo lectura</option>
            </select>
            <div>
              <input placeholder="Contraseña" type="password" {...createForm.register('password')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
              {createForm.formState.errors.password && <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.password.message}</p>}
            </div>
          </div>
          <div className="mt-3 flex items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" {...createForm.register('canEditPhone')} className="rounded border-gray-300" />
              Puede editar número de teléfono
            </label>
          </div>
          <AgentSelector selected={selectedAgentIds} onChange={setSelectedAgentIds} />
          <PermissionsTable selected={selectedPermissions} onChange={setSelectedPermissions} />
          <div className="mt-4 flex items-center gap-2">
            <button type="submit" disabled={createMutation.isPending}
              className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {createMutation.isPending && <Loader2 className="h-3 w-3 animate-spin" />} Guardar
            </button>
            <button type="button" onClick={() => setShowForm(false)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50">
              Cancelar
            </button>
          </div>
        </form>
      )}

      {/* ── Editar usuario ── */}
      {editUser && (
        <form onSubmit={editForm.handleSubmit(onEditSubmit)} className="mb-4 rounded-lg border border-amber-200 bg-amber-50 p-4">
          <h4 className="mb-3 text-sm font-medium text-gray-900">Editar usuario: {editUser.fullName}</h4>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <input placeholder="Nombre completo" {...editForm.register('fullName')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
              {editForm.formState.errors.fullName && <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.fullName.message}</p>}
            </div>
            <div>
              <input placeholder="Email" type="email" {...editForm.register('email')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
              {editForm.formState.errors.email && <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.email.message}</p>}
            </div>
            <select {...editForm.register('role')} className="rounded-md border border-gray-300 px-3 py-2 text-sm">
              <option value="Cobros">Cobros</option>
              <option value="Supervisor">Supervisor</option>
              <option value="Admin">Admin</option>
              <option value="ReadOnly">Solo lectura</option>
            </select>
            <input placeholder="Nueva contraseña (dejar vacío para no cambiar)" type="password" {...editForm.register('password')} className="rounded-md border border-gray-300 px-3 py-2 text-sm" />
          </div>
          <div className="mt-3 flex items-center gap-4">
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" {...editForm.register('isActive')} className="rounded border-gray-300" /> Activo
            </label>
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" {...editForm.register('canEditPhone')} className="rounded border-gray-300" /> Puede editar número de teléfono
            </label>
          </div>
          <AgentSelector selected={editSelectedAgentIds} onChange={setEditSelectedAgentIds} />
          <PermissionsTable selected={editSelectedPermissions} onChange={setEditSelectedPermissions} />
          <div className="mt-4 flex items-center gap-2">
            <button type="submit" disabled={updateMutation.isPending}
              className="flex items-center gap-1.5 rounded-md bg-amber-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-700 disabled:opacity-50">
              {updateMutation.isPending && <Loader2 className="h-3 w-3 animate-spin" />} Actualizar
            </button>
            <button type="button" onClick={() => setEditUser(null)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50">
              Cancelar
            </button>
          </div>
        </form>
      )}

      {createMutation.isError && (
        <div className="mb-3 flex items-center gap-1.5 text-xs text-red-600">
          <AlertCircle className="h-3.5 w-3.5" /><span>Error al crear el usuario.</span>
        </div>
      )}

      {/* ── Tabla de usuarios ── */}
      {isLoading ? (
        <div className="flex items-center justify-center py-8"><Loader2 className="h-6 w-6 animate-spin text-gray-400" /></div>
      ) : users && users.length > 0 ? (
        <div className="overflow-hidden rounded-lg bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Nombre</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Email</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Rol</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Permisos</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Estado</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {users.map(u => (
                <tr key={u.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">{u.fullName}</td>
                  <td className="px-4 py-3 text-sm text-gray-500">{u.email}</td>
                  <td className="px-4 py-3"><Badge variant={u.role}>{u.role}</Badge></td>
                  <td className="px-4 py-3">
                    {/* Botón de permisos — siempre visible */}
                    <button
                      onClick={() => setPermissionsUser(u)}
                      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium transition-colors ${
                        (u.permissions ?? []).length > 0
                          ? 'border-blue-200 bg-blue-50 text-blue-600 hover:bg-blue-100'
                          : 'border-gray-200 bg-gray-50 text-gray-400 hover:bg-gray-100 hover:text-gray-600'
                      }`}
                      title="Ver y editar permisos"
                    >
                      <ShieldCheck className="h-3.5 w-3.5" />
                      {(u.permissions ?? []).length > 0
                        ? `${u.permissions.length} permiso${u.permissions.length !== 1 ? 's' : ''}`
                        : 'Sin permisos'}
                    </button>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${u.isActive ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                      {u.isActive ? 'Activo' : 'Inactivo'}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1">
                      <button onClick={() => { openEdit(u); setShowForm(false) }}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors" title="Editar">
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button onClick={() => setDeleteTarget(u)}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors" title="Eliminar">
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

      {/* ── Modal de permisos ── */}
      {permissionsUser && (
        <PermissionsModal
          user={permissionsUser}
          onClose={() => setPermissionsUser(null)}
        />
      )}

      {/* ── Confirmar eliminar ── */}
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
