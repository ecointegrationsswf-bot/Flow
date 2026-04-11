import { useState, useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Loader2, Eye, EyeOff, ShieldCheck, KeyRound } from 'lucide-react'
import { useAuthStore } from '@/shared/stores/authStore'
import type { AuthUser } from '@/shared/stores/authStore'
import { api } from '@/shared/api/client'

const loginSchema = z.object({
  email: z.string().email('Email invalido'),
  password: z.string().min(1, 'La contrasena es requerida'),
})
type LoginForm = z.infer<typeof loginSchema>

const changePwSchema = z.object({
  newPassword: z.string().min(8, 'Minimo 8 caracteres'),
  confirmPassword: z.string().min(8, 'Confirma la contrasena'),
}).refine(d => d.newPassword === d.confirmPassword, { message: 'Las contrasenas no coinciden', path: ['confirmPassword'] })
type ChangePwForm = z.infer<typeof changePwSchema>

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

type Step = 'login' | 'changePassword' | 'verify2fa'

export function LoginPage() {
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.login)
  const [step, setStep] = useState<Step>('login')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [tempToken, setTempToken] = useState('')
  const [maskedEmail, setMaskedEmail] = useState('')
  const [otpCode, setOtpCode] = useState('')
  const [pwValue, setPwValue] = useState('')
  const [failedAttempts, setFailedAttempts] = useState(0)
  const [lockUntil, setLockUntil] = useState<number | null>(null)

  const isLocked = lockUntil !== null && Date.now() < lockUntil
  const tempTokenTimer = useRef<ReturnType<typeof setTimeout>>()

  // Clear tempToken after 10 min inactivity
  useEffect(() => {
    if (tempToken) {
      clearTimeout(tempTokenTimer.current)
      tempTokenTimer.current = setTimeout(() => {
        setTempToken(''); setStep('login'); setError('Sesion expirada. Inicia sesion de nuevo.')
      }, 10 * 60 * 1000)
    }
    return () => clearTimeout(tempTokenTimer.current)
  }, [tempToken])

  // Login form
  const loginForm = useForm<LoginForm>({ resolver: zodResolver(loginSchema) })

  // Change password form
  const changePwForm = useForm<ChangePwForm>({ resolver: zodResolver(changePwSchema) })

  const onLogin = async (data: LoginForm) => {
    if (isLocked) { setError(`Demasiados intentos. Espera ${Math.ceil(((lockUntil ?? 0) - Date.now()) / 1000)}s.`); return }
    setError(null); setLoading(true)
    try {
      const { data: res } = await api.post('/auth/login', data)
      setFailedAttempts(0); setLockUntil(null)
      if (res.requiresPasswordChange) {
        setTempToken(res.tempToken); setStep('changePassword')
      } else if (res.requires2FA) {
        setTempToken(res.tempToken); setMaskedEmail(res.email); setStep('verify2fa')
      } else if (res.token) {
        await finishLogin(res)
      }
    } catch (err: unknown) {
      const a = err as { response?: { data?: { error?: string }; status?: number } }
      const newAttempts = failedAttempts + 1
      setFailedAttempts(newAttempts)
      if (newAttempts >= 5) {
        const lockMs = Math.min(60000, newAttempts * 10000)
        setLockUntil(Date.now() + lockMs)
        setError(`Demasiados intentos. Espera ${lockMs / 1000}s.`)
      } else {
        setError(a.response?.status === 401 ? (a.response.data?.error ?? 'Credenciales invalidas.') : 'Error de conexion.')
      }
    } finally { setLoading(false) }
  }

  const onChangePassword = async (data: ChangePwForm) => {
    setError(null); setLoading(true)
    try {
      const { data: res } = await api.post('/auth/change-password', { tempToken, newPassword: data.newPassword })
      if (res.requires2FA) {
        setTempToken(res.tempToken); setMaskedEmail(res.email); setStep('verify2fa')
      }
    } catch (err: unknown) {
      const a = err as { response?: { data?: { error?: string } } }
      setError(a.response?.data?.error ?? 'Error al cambiar contrasena.')
    } finally { setLoading(false) }
  }

  const onVerify2FA = async () => {
    if (otpCode.length !== 6) { setError('Ingresa el codigo de 6 digitos.'); return }
    setError(null); setLoading(true)
    try {
      const { data: res } = await api.post('/auth/verify-2fa', { tempToken, code: otpCode })
      await finishLogin(res)
    } catch (err: unknown) {
      const a = err as { response?: { data?: { error?: string } } }
      setError(a.response?.data?.error ?? 'Codigo invalido.')
    } finally { setLoading(false) }
  }

  const onResend = async () => {
    try {
      await api.post('/auth/resend-2fa', { tempToken, code: '' })
      setError(null)
    } catch { /* ignore */ }
  }

  const finishLogin = async (res: { token: string; tenantId: string; user: Record<string, unknown> }) => {
    const user: AuthUser = {
      ...(res.user as Omit<AuthUser, 'permissions'>),
      permissions: Array.isArray(res.user.permissions) ? (res.user.permissions as string[]) : [],
    }
    localStorage.setItem('token', res.token)
    localStorage.setItem('tenantId', res.tenantId)
    localStorage.setItem('user', JSON.stringify(user))
    useAuthStore.setState({
      token: res.token, tenantId: res.tenantId, user, isAuthenticated: true,
    })
    navigate('/dashboard', { replace: true })
  }

  const inputClass = "block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50">
      <div className="w-full max-w-sm rounded-lg bg-white p-8 shadow-md">
        <div className="mb-6 flex flex-col items-center">
          <img src="/logo.png" alt="TalkIA" className="h-14 w-14 rounded" />
          <h1 className="mt-3 text-xl font-bold text-gray-900">TalkIA</h1>
          <p className="text-sm text-gray-500">Plataforma de agentes IA</p>
        </div>

        {error && <div className="mb-4 rounded-md bg-red-50 p-3 text-center text-sm text-red-600">{error}</div>}

        {/* Step 1: Login */}
        {step === 'login' && (
          <form onSubmit={loginForm.handleSubmit(onLogin)} className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Email</label>
              <input type="email" {...loginForm.register('email')} className={inputClass} placeholder="usuario@ejemplo.com" />
              {loginForm.formState.errors.email && <p className="mt-1 text-xs text-red-600">{loginForm.formState.errors.email.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Contrasena</label>
              <div className="relative mt-1">
                <input type={showPassword ? 'text' : 'password'} {...loginForm.register('password')} className={`${inputClass} pr-10`} placeholder="••••••••" />
                <button type="button" onClick={() => setShowPassword(!showPassword)} className="absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600">
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </div>
            <button type="submit" disabled={loading || isLocked} className="flex w-full items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {loading && <Loader2 className="h-4 w-4 animate-spin" />}
              Iniciar sesion
            </button>
            <div className="text-center">
              <button type="button" onClick={() => navigate('/forgot-password')} className="text-xs text-blue-600 hover:underline">
                Olvidaste tu contrasena?
              </button>
            </div>
          </form>
        )}

        {/* Step 2: Change Password */}
        {step === 'changePassword' && (
          <form onSubmit={changePwForm.handleSubmit(onChangePassword)} className="space-y-4">
            <div className="flex items-center gap-2 rounded-md bg-amber-50 p-3 text-sm text-amber-700">
              <KeyRound className="h-5 w-5 shrink-0" />
              Debes cambiar tu contrasena antes de continuar.
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Nueva contrasena</label>
              <div className="relative mt-1">
                <input type={showPassword ? 'text' : 'password'} {...changePwForm.register('newPassword', { onChange: e => setPwValue(e.target.value) })} className={`${inputClass} pr-10`} />
                <button type="button" onClick={() => setShowPassword(!showPassword)} className="absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600">
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
              {pwValue && (() => { const s = getStrength(pwValue); return (
                <div className="mt-1.5">
                  <div className="h-1.5 w-full rounded-full bg-gray-200"><div className={`h-1.5 rounded-full transition-all ${s.color} ${s.width}`} /></div>
                  <p className={`mt-0.5 text-xs ${s.text}`}>{s.label}</p>
                </div>
              )})()}
              {changePwForm.formState.errors.newPassword && <p className="mt-1 text-xs text-red-600">{changePwForm.formState.errors.newPassword.message}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Confirmar contrasena</label>
              <input type="password" {...changePwForm.register('confirmPassword')} className={inputClass} />
              {changePwForm.formState.errors.confirmPassword && <p className="mt-1 text-xs text-red-600">{changePwForm.formState.errors.confirmPassword.message}</p>}
            </div>
            <button type="submit" disabled={loading} className="flex w-full items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {loading && <Loader2 className="h-4 w-4 animate-spin" />}
              Cambiar contrasena
            </button>
          </form>
        )}

        {/* Step 3: 2FA */}
        {step === 'verify2fa' && (
          <div className="space-y-4">
            <div className="flex items-center gap-2 rounded-md bg-blue-50 p-3 text-sm text-blue-700">
              <ShieldCheck className="h-5 w-5 shrink-0" />
              Enviamos un codigo de verificacion a {maskedEmail}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Codigo de 6 digitos</label>
              <input
                type="text"
                maxLength={6}
                value={otpCode}
                onChange={e => setOtpCode(e.target.value.replace(/\D/g, ''))}
                className={`${inputClass} mt-1 text-center text-2xl tracking-[0.5em] font-mono`}
                placeholder="000000"
                autoFocus
              />
            </div>
            <button onClick={onVerify2FA} disabled={loading || otpCode.length !== 6} className="flex w-full items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {loading && <Loader2 className="h-4 w-4 animate-spin" />}
              Verificar
            </button>
            <button onClick={onResend} className="w-full text-center text-xs text-gray-500 hover:text-blue-600">
              Reenviar codigo
            </button>
          </div>
        )}

        <div className="mt-4 text-center">
          <button type="button" onClick={() => navigate('/admin/login')} className="text-xs text-gray-400 hover:text-gray-600">
            Administrador
          </button>
        </div>
      </div>
    </div>
  )
}
