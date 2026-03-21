import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { X, Loader2, KeyRound, UserPlus, Eye, EyeOff } from 'lucide-react'
import {
  useAdminTenantUsers,
  useCreateTenantUser,
  useChangeTenantUserPassword,
  type AdminTenant,
} from '@/modules/admin/hooks/useAdminTenants'

const newUserSchema = z.object({
  fullName: z.string().min(1, 'El nombre es requerido'),
  email: z.string().email('Email invalido'),
  password: z.string().min(8, 'Minimo 8 caracteres'),
  role: z.string().min(1, 'El rol es requerido'),
})

type NewUserForm = z.infer<typeof newUserSchema>

function getPasswordStrength(pw: string): { label: string; color: string; width: string; textColor: string } {
  if (!pw || pw.length < 8) return { label: 'Debil', color: 'bg-red-500', width: 'w-1/3', textColor: 'text-red-600' }
  let score = 0
  if (pw.length >= 8) score++
  if (pw.length >= 12) score++
  if (/[A-Z]/.test(pw)) score++
  if (/[a-z]/.test(pw)) score++
  if (/[0-9]/.test(pw)) score++
  if (/[^A-Za-z0-9]/.test(pw)) score++
  if (score <= 3) return { label: 'Debil', color: 'bg-red-500', width: 'w-1/3', textColor: 'text-red-600' }
  if (score <= 4) return { label: 'Media', color: 'bg-yellow-500', width: 'w-2/3', textColor: 'text-yellow-600' }
  return { label: 'Fuerte', color: 'bg-green-500', width: 'w-full', textColor: 'text-green-600' }
}

function PasswordStrengthBar({ password }: { password: string }) {
  if (!password) return null
  const s = getPasswordStrength(password)
  return (
    <div className="mt-1">
      <div className="h-1.5 w-full rounded-full bg-gray-200">
        <div className={`h-1.5 rounded-full transition-all ${s.color} ${s.width}`} />
      </div>
      <p className={`mt-0.5 text-xs ${s.textColor}`}>{s.label}</p>
    </div>
  )
}

interface TenantUsersModalProps {
  tenant: AdminTenant
  onClose: () => void
}

export function TenantUsersModal({ tenant, onClose }: TenantUsersModalProps) {
  const { data: users, isLoading } = useAdminTenantUsers(tenant.id)
  const createUser = useCreateTenantUser()
  const changePassword = useChangeTenantUserPassword()

  const [showNewUser, setShowNewUser] = useState(false)
  const [createPwValue, setCreatePwValue] = useState('')
  const [showCreatePw, setShowCreatePw] = useState(false)
  const [showChangePw, setShowChangePw] = useState(false)
  const [passwordUserId, setPasswordUserId] = useState<string | null>(null)
  const [newPassword, setNewPassword] = useState('')
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [createError, setCreateError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<NewUserForm>({
    resolver: zodResolver(newUserSchema),
    defaultValues: { fullName: '', email: '', password: '', role: 'Cobros' },
  })

  const onCreateUser = async (data: NewUserForm) => {
    setCreateError(null)
    try {
      await createUser.mutateAsync({ tenantId: tenant.id, ...data })
      reset()
      setShowNewUser(false)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setCreateError(axiosErr.response?.data?.error ?? 'Error al crear usuario.')
    }
  }

  const onChangePassword = async (userId: string) => {
    setPasswordError(null)
    if (newPassword.length < 8) {
      setPasswordError('Minimo 8 caracteres')
      return
    }
    try {
      await changePassword.mutateAsync({
        tenantId: tenant.id,
        userId,
        newPassword,
      })
      setPasswordUserId(null)
      setNewPassword('')
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setPasswordError(axiosErr.response?.data?.error ?? 'Error al cambiar contrasena.')
    }
  }

  const formatDate = (dateStr: string | null) => {
    if (!dateStr) return 'Nunca'
    return new Date(dateStr).toLocaleDateString('es-PA', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    })
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-3xl rounded-lg bg-white shadow-xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">
              Usuarios de {tenant.name}
            </h2>
            <p className="text-sm text-gray-500">{tenant.slug}</p>
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Body */}
        <div className="max-h-[60vh] overflow-auto px-6 py-4">
          {isLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-indigo-600" />
            </div>
          ) : (
            <table className="min-w-full divide-y divide-gray-200">
              <thead>
                <tr>
                  <th className="pb-2 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Nombre
                  </th>
                  <th className="pb-2 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Email
                  </th>
                  <th className="pb-2 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Rol
                  </th>
                  <th className="pb-2 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Estado
                  </th>
                  <th className="pb-2 text-left text-xs font-medium uppercase tracking-wider text-gray-500">
                    Ultimo acceso
                  </th>
                  <th className="pb-2 text-right text-xs font-medium uppercase tracking-wider text-gray-500">
                    Acciones
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {users?.map((user) => (
                  <tr key={user.id}>
                    <td className="py-2 text-sm text-gray-900">{user.fullName}</td>
                    <td className="py-2 text-sm text-gray-600">{user.email}</td>
                    <td className="py-2">
                      <span className="inline-flex rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700">
                        {user.role}
                      </span>
                    </td>
                    <td className="py-2">
                      <span
                        className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                          user.isActive
                            ? 'bg-green-100 text-green-700'
                            : 'bg-red-100 text-red-700'
                        }`}
                      >
                        {user.isActive ? 'Activo' : 'Inactivo'}
                      </span>
                    </td>
                    <td className="py-2 text-xs text-gray-500">
                      {formatDate(user.lastLoginAt)}
                    </td>
                    <td className="py-2 text-right">
                      {passwordUserId === user.id ? (
                        <div className="space-y-1">
                          <div className="flex items-center justify-end gap-2">
                            <div className="relative">
                              <input
                                type={showChangePw ? 'text' : 'password'}
                                value={newPassword}
                                onChange={(e) => setNewPassword(e.target.value)}
                                placeholder="Min. 8 caracteres"
                                className="w-40 rounded-md border border-gray-300 px-2 py-1 pr-7 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                              />
                              <button
                                type="button"
                                onClick={() => setShowChangePw(!showChangePw)}
                                className="absolute inset-y-0 right-0 flex items-center pr-1.5 text-gray-400 hover:text-gray-600"
                              >
                                {showChangePw ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                              </button>
                            </div>
                            <button
                              onClick={() => onChangePassword(user.id)}
                              disabled={changePassword.isPending}
                              className="rounded bg-indigo-600 px-2 py-1 text-xs text-white hover:bg-indigo-700 disabled:opacity-50"
                            >
                              {changePassword.isPending ? '...' : 'Guardar'}
                            </button>
                            <button
                              onClick={() => {
                                setPasswordUserId(null)
                                setNewPassword('')
                                setPasswordError(null)
                                setShowChangePw(false)
                              }}
                              className="rounded px-2 py-1 text-xs text-gray-500 hover:text-gray-700"
                            >
                              Cancelar
                            </button>
                          </div>
                          <div className="flex justify-end"><div className="w-40"><PasswordStrengthBar password={newPassword} /></div></div>
                        </div>
                      ) : (
                        <button
                          onClick={() => {
                            setPasswordUserId(user.id)
                            setNewPassword('')
                            setPasswordError(null)
                          }}
                          className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                          title="Cambiar contrasena"
                        >
                          <KeyRound className="h-4 w-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
                {users?.length === 0 && (
                  <tr>
                    <td colSpan={6} className="py-6 text-center text-sm text-gray-500">
                      No hay usuarios en este tenant
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}

          {passwordError && (
            <div className="mt-2 rounded-md bg-red-50 p-2 text-sm text-red-600">
              {passwordError}
            </div>
          )}

          {/* Add user form */}
          <div className="mt-4 border-t border-gray-200 pt-4">
            {showNewUser ? (
              <div>
                <h3 className="mb-3 text-sm font-semibold text-gray-700">Agregar usuario</h3>
                {createError && (
                  <div className="mb-3 rounded-md bg-red-50 p-2 text-sm text-red-600">
                    {createError}
                  </div>
                )}
                <form onSubmit={handleSubmit(onCreateUser)} className="space-y-3">
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-700">
                        Nombre completo
                      </label>
                      <input
                        {...register('fullName')}
                        className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                      />
                      {errors.fullName && (
                        <p className="mt-1 text-xs text-red-600">{errors.fullName.message}</p>
                      )}
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-700">Email</label>
                      <input
                        type="email"
                        {...register('email')}
                        className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                      />
                      {errors.email && (
                        <p className="mt-1 text-xs text-red-600">{errors.email.message}</p>
                      )}
                    </div>
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-700">
                        Contrasena (min. 8 caracteres)
                      </label>
                      <div className="relative mt-1">
                        <input
                          type={showCreatePw ? 'text' : 'password'}
                          {...register('password', { onChange: (e) => setCreatePwValue(e.target.value) })}
                          className="block w-full rounded-md border border-gray-300 px-3 py-1.5 pr-9 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                        />
                        <button
                          type="button"
                          onClick={() => setShowCreatePw(!showCreatePw)}
                          className="absolute inset-y-0 right-0 flex items-center pr-2 text-gray-400 hover:text-gray-600"
                        >
                          {showCreatePw ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                        </button>
                      </div>
                      <PasswordStrengthBar password={createPwValue} />
                      {errors.password && (
                        <p className="mt-1 text-xs text-red-600">{errors.password.message}</p>
                      )}
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-700">Rol</label>
                      <select
                        {...register('role')}
                        className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                      >
                        <option value="Admin">Admin</option>
                        <option value="Supervisor">Supervisor</option>
                        <option value="Cobros">Cobros</option>
                        <option value="ReadOnly">Solo lectura</option>
                      </select>
                      {errors.role && (
                        <p className="mt-1 text-xs text-red-600">{errors.role.message}</p>
                      )}
                    </div>
                  </div>
                  <div className="flex justify-end gap-2">
                    <button
                      type="button"
                      onClick={() => {
                        setShowNewUser(false)
                        setCreateError(null)
                        reset()
                      }}
                      className="rounded-md border border-gray-300 px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
                    >
                      Cancelar
                    </button>
                    <button
                      type="submit"
                      disabled={createUser.isPending}
                      className="flex items-center gap-1 rounded-md bg-indigo-600 px-3 py-1.5 text-sm text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                      {createUser.isPending && (
                        <Loader2 className="h-3 w-3 animate-spin" />
                      )}
                      Crear usuario
                    </button>
                  </div>
                </form>
              </div>
            ) : (
              <button
                onClick={() => setShowNewUser(true)}
                className="flex items-center gap-2 text-sm font-medium text-indigo-600 hover:text-indigo-500"
              >
                <UserPlus className="h-4 w-4" />
                Agregar usuario
              </button>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="flex justify-end border-t border-gray-200 px-6 py-3">
          <button
            onClick={onClose}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Cerrar
          </button>
        </div>
      </div>
    </div>
  )
}
