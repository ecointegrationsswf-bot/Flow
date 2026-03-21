import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Loader2, Eye, EyeOff, CheckCircle } from 'lucide-react'
import { api } from '@/shared/api/client'

function getStrength(pw: string) {
  if (!pw || pw.length < 8) return { label: 'Debil', color: 'bg-red-500', width: 'w-1/3', text: 'text-red-600' }
  let s = 0
  if (pw.length >= 8) s++; if (pw.length >= 12) s++
  if (/[A-Z]/.test(pw)) s++; if (/[a-z]/.test(pw)) s++
  if (/[0-9]/.test(pw)) s++; if (/[^A-Za-z0-9]/.test(pw)) s++
  if (s <= 3) return { label: 'Debil', color: 'bg-red-500', width: 'w-1/3', text: 'text-red-600' }
  if (s <= 4) return { label: 'Media', color: 'bg-yellow-500', width: 'w-2/3', text: 'text-yellow-600' }
  return { label: 'Fuerte', color: 'bg-green-500', width: 'w-full', text: 'text-green-600' }
}

export function ResetPasswordPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''

  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    if (!token) { setError('Enlace invalido. Solicita un nuevo enlace.'); return }
    if (newPassword.length < 8) { setError('La contrasena debe tener al menos 8 caracteres.'); return }
    if (newPassword !== confirmPassword) { setError('Las contrasenas no coinciden.'); return }

    setLoading(true)
    try {
      await api.post('/auth/reset-password', { token, newPassword })
      setSuccess(true)
    } catch (err: unknown) {
      const a = err as { response?: { data?: { error?: string } } }
      setError(a.response?.data?.error ?? 'Error al restablecer la contrasena.')
    } finally {
      setLoading(false)
    }
  }

  const inputClass = "block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50">
      <div className="w-full max-w-sm rounded-lg bg-white p-8 shadow-md">
        <div className="mb-6 flex flex-col items-center">
          <img src="/logo.png" alt="TalkIA" className="h-14 w-14 rounded" />
          <h1 className="mt-3 text-xl font-bold text-gray-900">TalkIA</h1>
          <p className="text-sm text-gray-500">Restablecer contrasena</p>
        </div>

        {success ? (
          <div className="space-y-4">
            <div className="flex flex-col items-center gap-3 rounded-md bg-green-50 p-4 text-center">
              <CheckCircle className="h-8 w-8 text-green-600" />
              <p className="text-sm text-green-700">Tu contrasena ha sido actualizada correctamente.</p>
            </div>
            <button
              onClick={() => navigate('/login')}
              className="flex w-full items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
            >
              Iniciar sesion
            </button>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4">
            {error && <div className="rounded-md bg-red-50 p-3 text-center text-sm text-red-600">{error}</div>}

            <div>
              <label className="block text-sm font-medium text-gray-700">Nueva contrasena</label>
              <div className="relative mt-1">
                <input
                  type={showPassword ? 'text' : 'password'}
                  value={newPassword}
                  onChange={e => setNewPassword(e.target.value)}
                  className={`${inputClass} pr-10`}
                />
                <button type="button" onClick={() => setShowPassword(!showPassword)} className="absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600">
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
              {newPassword && (() => { const s = getStrength(newPassword); return (
                <div className="mt-1.5">
                  <div className="h-1.5 w-full rounded-full bg-gray-200"><div className={`h-1.5 rounded-full transition-all ${s.color} ${s.width}`} /></div>
                  <p className={`mt-0.5 text-xs ${s.text}`}>{s.label}</p>
                </div>
              )})()}
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700">Confirmar contrasena</label>
              <input
                type="password"
                value={confirmPassword}
                onChange={e => setConfirmPassword(e.target.value)}
                className={`mt-1 ${inputClass}`}
              />
            </div>

            <button type="submit" disabled={loading} className="flex w-full items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {loading && <Loader2 className="h-4 w-4 animate-spin" />}
              Restablecer contrasena
            </button>
          </form>
        )}
      </div>
    </div>
  )
}
