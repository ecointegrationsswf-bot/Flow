import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Users, Plus, Pencil, Power, Eye, EyeOff, X, Loader2 } from 'lucide-react'
import {
  useAdminUsers,
  useCreateAdminUser,
  useUpdateAdminUser,
  type AdminUser,
} from '@/modules/admin/hooks/useAdminUsers'

// ── Password strength ───────────────────────────────
function getPasswordStrength(pw: string): { level: 'weak' | 'medium' | 'strong'; label: string; color: string; width: string } {
  if (!pw || pw.length < 8) return { level: 'weak', label: 'Debil', color: 'bg-red-500', width: 'w-1/3' }
  let score = 0
  if (pw.length >= 8) score++
  if (pw.length >= 12) score++
  if (/[A-Z]/.test(pw)) score++
  if (/[a-z]/.test(pw)) score++
  if (/[0-9]/.test(pw)) score++
  if (/[^A-Za-z0-9]/.test(pw)) score++
  if (score <= 3) return { level: 'weak', label: 'Debil', color: 'bg-red-500', width: 'w-1/3' }
  if (score <= 4) return { level: 'medium', label: 'Media', color: 'bg-yellow-500', width: 'w-2/3' }
  return { level: 'strong', label: 'Fuerte', color: 'bg-green-500', width: 'w-full' }
}

function PasswordStrengthBar({ password }: { password: string }) {
  const s = getPasswordStrength(password)
  return (
    <div className="mt-1.5">
      <div className="h-1.5 w-full rounded-full bg-gray-200">
        <div className={`h-1.5 rounded-full transition-all ${s.color} ${s.width}`} />
      </div>
      <p className={`mt-0.5 text-xs ${s.level === 'weak' ? 'text-red-600' : s.level === 'medium' ? 'text-yellow-600' : 'text-green-600'}`}>
        {s.label}
      </p>
    </div>
  )
}

// ── Create schema ───────────────────────────────────
const createSchema = z.object({
  fullName: z.string().min(2, 'Nombre requerido'),
  email: z.string().email('Email invalido'),
  password: z.string().min(8, 'Minimo 8 caracteres'),
})
type CreateForm = z.infer<typeof createSchema>

// ── Form Modal ──────────────────────────────────────
function AdminUserFormModal({ user, onClose }: { user?: AdminUser; onClose: () => void }) {
  const isEdit = !!user
  const createMut = useCreateAdminUser()
  const updateMut = useUpdateAdminUser()
  const [error, setError] = useState<string | null>(null)
  const [showPw, setShowPw] = useState(false)
  const [password, setPassword] = useState('')

  const { register, handleSubmit, formState: { errors } } = useForm<CreateForm>({
    resolver: zodResolver(isEdit ? createSchema.partial({ password: true }) : createSchema),
    defaultValues: isEdit
      ? { fullName: user.fullName, email: user.email, password: '' }
      : { fullName: '', email: '', password: '' },
  })

  const onSubmit = async (data: CreateForm) => {
    setError(null)
    try {
      if (isEdit) {
        const payload: Record<string, unknown> = { id: user.id, fullName: data.fullName, email: data.email }
        if (data.password) payload.password = data.password
        await updateMut.mutateAsync(payload as { id: string; fullName: string; email: string; password?: string })
      } else {
        await createMut.mutateAsync(data)
      }
      onClose()
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setError(axiosErr.response?.data?.error ?? 'Error al guardar.')
    }
  }

  const isPending = createMut.isPending || updateMut.isPending

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Editar Administrador' : 'Nuevo Administrador'}</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600"><X className="h-5 w-5" /></button>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 px-6 py-4">
          {error && <div className="rounded-md bg-red-50 p-3 text-sm text-red-600">{error}</div>}

          <div>
            <label className="block text-sm font-medium text-gray-700">Nombre completo *</label>
            <input {...register('fullName')} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
            {errors.fullName && <p className="mt-1 text-xs text-red-600">{errors.fullName.message}</p>}
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Email *</label>
            <input type="email" {...register('email')} className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500" />
            {errors.email && <p className="mt-1 text-xs text-red-600">{errors.email.message}</p>}
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">
              Contrasena {isEdit ? '(dejar vacio para no cambiar)' : '*'}
            </label>
            <div className="relative mt-1">
              <input
                type={showPw ? 'text' : 'password'}
                {...register('password', { onChange: (e) => setPassword(e.target.value) })}
                className="block w-full rounded-md border border-gray-300 px-3 py-2 pr-10 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                placeholder={isEdit ? '' : 'Minimo 8 caracteres'}
              />
              <button
                type="button"
                onClick={() => setShowPw(!showPw)}
                className="absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600"
              >
                {showPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
            {password && <PasswordStrengthBar password={password} />}
            {errors.password && <p className="mt-1 text-xs text-red-600">{errors.password.message}</p>}
          </div>

          <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
            <button type="button" onClick={onClose} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">Cancelar</button>
            <button type="submit" disabled={isPending} className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors">
              {isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              {isEdit ? 'Guardar' : 'Crear'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Main Page ───────────────────────────────────────
export function AdminUsersPage() {
  const { data: users, isLoading } = useAdminUsers()
  const updateMut = useUpdateAdminUser()
  const [showForm, setShowForm] = useState(false)
  const [editUser, setEditUser] = useState<AdminUser | undefined>()

  const handleToggle = async (user: AdminUser) => {
    await updateMut.mutateAsync({ id: user.id, isActive: !user.isActive })
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Administradores</h1>
          <p className="text-sm text-gray-500">Usuarios del panel administrativo</p>
        </div>
        <button
          onClick={() => { setEditUser(undefined); setShowForm(true) }}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Nuevo administrador
        </button>
      </div>

      {isLoading ? (
        <div className="py-12 text-center text-gray-400">Cargando...</div>
      ) : !users?.length ? (
        <div className="py-16 text-center">
          <Users className="mx-auto h-12 w-12 text-gray-300" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">Sin administradores</h3>
        </div>
      ) : (
        <div className="rounded-lg bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Nombre</th>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Email</th>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Estado</th>
                <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">Ultimo acceso</th>
                <th className="px-6 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {users.map((user) => (
                <tr key={user.id}>
                  <td className="whitespace-nowrap px-6 py-4 text-sm font-medium text-gray-900">{user.fullName}</td>
                  <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-500">{user.email}</td>
                  <td className="whitespace-nowrap px-6 py-4">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${user.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                      {user.isActive ? 'Activo' : 'Inactivo'}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-6 py-4 text-sm text-gray-500">
                    {user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleDateString() : '—'}
                  </td>
                  <td className="whitespace-nowrap px-6 py-4 text-right">
                    <div className="flex items-center justify-end gap-1">
                      <button
                        onClick={() => { setEditUser(user); setShowForm(true) }}
                        title="Editar"
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => handleToggle(user)}
                        title={user.isActive ? 'Deshabilitar' : 'Habilitar'}
                        className={`rounded-lg p-1.5 transition-colors ${user.isActive ? 'text-gray-400 hover:bg-gray-100 hover:text-red-600' : 'text-gray-400 hover:bg-gray-100 hover:text-green-600'}`}
                      >
                        <Power className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showForm && <AdminUserFormModal user={editUser} onClose={() => setShowForm(false)} />}
    </div>
  )
}
