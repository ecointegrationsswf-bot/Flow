import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Loader2, ArrowLeft, Mail } from 'lucide-react'
import { api } from '@/shared/api/client'

export function ForgotPasswordPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [loading, setLoading] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!email) { setError('Ingresa tu correo electronico.'); return }
    setError(null); setLoading(true)
    try {
      await api.post('/auth/forgot-password', { email })
      setSent(true)
    } catch {
      setError('Error al enviar el correo. Intenta nuevamente.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50">
      <div className="w-full max-w-sm rounded-lg bg-white p-8 shadow-md">
        <div className="mb-6 flex flex-col items-center">
          <img src="/logo.png" alt="TalkIA" className="h-14 w-14 rounded" />
          <h1 className="mt-3 text-xl font-bold text-gray-900">TalkIA</h1>
        </div>

        {sent ? (
          <div className="space-y-4">
            <div className="flex flex-col items-center gap-3 rounded-md bg-green-50 p-4 text-center">
              <Mail className="h-8 w-8 text-green-600" />
              <p className="text-sm text-green-700">
                Si el correo <strong>{email}</strong> esta registrado, recibiras un enlace para restablecer tu contrasena.
              </p>
            </div>
            <p className="text-center text-xs text-gray-500">
              Revisa tu bandeja de entrada y la carpeta de spam.
            </p>
            <button
              onClick={() => navigate('/login')}
              className="flex w-full items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
            >
              Volver al login
            </button>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="rounded-md bg-blue-50 p-3 text-center text-sm text-blue-700">
              Ingresa tu correo electronico y te enviaremos un enlace para restablecer tu contrasena.
            </div>

            {error && <div className="rounded-md bg-red-50 p-3 text-center text-sm text-red-600">{error}</div>}

            <div>
              <label className="block text-sm font-medium text-gray-700">Email</label>
              <input
                type="email"
                value={email}
                onChange={e => setEmail(e.target.value)}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                placeholder="usuario@ejemplo.com"
                autoFocus
              />
            </div>

            <button type="submit" disabled={loading} className="flex w-full items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {loading && <Loader2 className="h-4 w-4 animate-spin" />}
              Enviar enlace
            </button>

            <button type="button" onClick={() => navigate('/login')} className="flex w-full items-center justify-center gap-1 text-sm text-gray-500 hover:text-gray-700">
              <ArrowLeft className="h-3.5 w-3.5" /> Volver al login
            </button>
          </form>
        )}
      </div>
    </div>
  )
}
