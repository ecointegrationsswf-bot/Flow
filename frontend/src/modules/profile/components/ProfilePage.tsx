import { useState, useRef, useEffect } from 'react'
import { Camera, Trash2, Upload, Loader2, KeyRound, Eye, EyeOff, Mail, Shield, Calendar, User, Pencil, Phone } from 'lucide-react'
import { useProfile, useUpdateProfile, useUploadAvatar, useDeleteAvatar, useChangePassword } from '@/shared/hooks/useProfile'

function getInitials(name: string): string {
  return name
    .split(' ')
    .map((w) => w[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

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

function initialsColor(name: string): string {
  const colors = [
    'from-blue-500 to-blue-600', 'from-emerald-500 to-emerald-600', 'from-purple-500 to-purple-600',
    'from-orange-500 to-orange-600', 'from-pink-500 to-pink-600', 'from-teal-500 to-teal-600',
    'from-indigo-500 to-indigo-600', 'from-rose-500 to-rose-600',
  ]
  let hash = 0
  for (let i = 0; i < name.length; i++) {
    hash = name.charCodeAt(i) + ((hash << 5) - hash)
  }
  return colors[Math.abs(hash) % colors.length]
}

export function ProfilePage() {
  const { data: profile, isLoading } = useProfile()
  const updateProfile = useUpdateProfile()
  const uploadAvatar = useUploadAvatar()
  const deleteAvatar = useDeleteAvatar()
  const changePassword = useChangePassword()

  const [fullName, setFullName] = useState('')
  const [notifyPhone, setNotifyPhone] = useState('')
  const [isEditingPhone, setIsEditingPhone] = useState(false)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showCurrentPw, setShowCurrentPw] = useState(false)
  const [showNewPw, setShowNewPw] = useState(false)
  const [pwError, setPwError] = useState<string | null>(null)
  const [pwSuccess, setPwSuccess] = useState(false)
  const [isEditingName, setIsEditingName] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const cameraInputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (profile) {
      setFullName(profile.fullName)
      setNotifyPhone(profile.notifyPhone ?? '')
    }
  }, [profile])

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) {
      uploadAvatar.mutate(file)
    }
    e.target.value = ''
  }

  const handleSave = () => {
    if (fullName.trim() && fullName !== profile?.fullName) {
      updateProfile.mutate({ fullName: fullName.trim() }, {
        onSuccess: () => setIsEditingName(false),
      })
    }
  }

  const handleSavePhone = () => {
    updateProfile.mutate(
      { fullName: profile?.fullName ?? fullName, notifyPhone: notifyPhone.trim() || null },
      { onSuccess: () => setIsEditingPhone(false) }
    )
  }

  const handleChangePassword = () => {
    setPwError(null)
    setPwSuccess(false)
    if (!currentPassword) { setPwError('Ingresa tu contrasena actual.'); return }
    if (newPassword.length < 8) { setPwError('La nueva contrasena debe tener al menos 8 caracteres.'); return }
    if (newPassword !== confirmPassword) { setPwError('Las contrasenas no coinciden.'); return }
    changePassword.mutate(
      { currentPassword, newPassword },
      {
        onSuccess: () => {
          setPwSuccess(true)
          setCurrentPassword(''); setNewPassword(''); setConfirmPassword('')
        },
        onError: (err: unknown) => {
          const a = err as { response?: { data?: { error?: string } } }
          setPwError(a.response?.data?.error ?? 'Error al cambiar la contrasena.')
        },
      }
    )
  }

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-blue-600" />
      </div>
    )
  }

  if (!profile) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="rounded-xl bg-red-50 p-6 text-center text-sm text-red-600">
          Error al cargar el perfil. Intenta recargar la pagina.
        </div>
      </div>
    )
  }

  const hasAvatar = !!profile.avatarUrl
  const initials = getInitials(profile.fullName)
  const bgGradient = initialsColor(profile.fullName)
  // Nuevos avatares: data URL base64 (usable directamente como src).
  // Legado: ruta blob → servir vía proxy de la API.
  const apiBase = import.meta.env.VITE_API_BASE_URL ?? '/api'
  const avatarSrc = hasAvatar
    ? profile.avatarUrl?.startsWith('data:')
      ? profile.avatarUrl
      : `${apiBase}/profile/avatar-img/${profile.id}?v=${encodeURIComponent(profile.avatarUrl ?? '')}`
    : null

  return (
    <div className="flex h-full flex-col overflow-y-auto bg-gray-50">
      {/* Banner superior - full width */}
      <div className="relative h-48 w-full flex-shrink-0 bg-gradient-to-r from-blue-600 via-blue-500 to-indigo-600">
        <div className="absolute inset-0 opacity-20" style={{
          backgroundImage: 'radial-gradient(circle at 20% 50%, rgba(255,255,255,0.3) 0%, transparent 50%), radial-gradient(circle at 80% 30%, rgba(255,255,255,0.2) 0%, transparent 50%), radial-gradient(circle at 50% 80%, rgba(255,255,255,0.15) 0%, transparent 40%)',
        }} />
        <div className="absolute inset-0 opacity-10" style={{
          backgroundImage: 'url("data:image/svg+xml,%3Csvg width=\'60\' height=\'60\' viewBox=\'0 0 60 60\' xmlns=\'http://www.w3.org/2000/svg\'%3E%3Cg fill=\'none\' fill-rule=\'evenodd\'%3E%3Cg fill=\'%23ffffff\' fill-opacity=\'0.4\'%3E%3Cpath d=\'M36 34v-4h-2v4h-4v2h4v4h2v-4h4v-2h-4zm0-30V0h-2v4h-4v2h4v4h2V6h4V4h-4zM6 34v-4H4v4H0v2h4v4h2v-4h4v-2H6zM6 4V0H4v4H0v2h4v4h2V6h4V4H6z\'/%3E%3C/g%3E%3C/g%3E%3C/svg%3E")',
        }} />
      </div>

      {/* Content area */}
      <div className="flex-1 px-6 pb-8 lg:px-12">
        <div className="mx-auto w-full max-w-6xl">
          {/* Avatar row - overlaps banner */}
          <div className="flex flex-col items-center sm:flex-row sm:items-end sm:gap-6" style={{ marginTop: '-4rem' }}>
            {/* Avatar */}
            <div className="relative flex-shrink-0">
              <div className="rounded-full border-4 border-white shadow-xl">
                {hasAvatar && avatarSrc ? (
                  <img
                    src={avatarSrc}
                    alt={profile.fullName}
                    className="h-32 w-32 rounded-full object-cover object-center"
                    style={{ minWidth: '8rem', minHeight: '8rem' }}
                  />
                ) : (
                  <div
                    className={`flex h-32 w-32 items-center justify-center rounded-full bg-gradient-to-br text-4xl font-bold text-white ${bgGradient}`}
                  >
                    {initials}
                  </div>
                )}
              </div>
              {(uploadAvatar.isPending || deleteAvatar.isPending) && (
                <div className="absolute inset-0 flex items-center justify-center rounded-full bg-black/40">
                  <Loader2 className="h-8 w-8 animate-spin text-white" />
                </div>
              )}
              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                className="absolute bottom-1 right-1 flex h-9 w-9 items-center justify-center rounded-full bg-blue-600 text-white shadow-lg transition-transform hover:scale-110 hover:bg-blue-700"
              >
                <Camera className="h-4 w-4" />
              </button>
            </div>

            {/* Name + role + photo buttons */}
            <div className="mt-4 flex flex-1 flex-col items-center sm:mt-6 sm:items-start sm:pb-2">
              {isEditingName ? (
                <div className="flex items-center gap-2">
                  <input
                    type="text"
                    value={fullName}
                    onChange={(e) => setFullName(e.target.value)}
                    autoFocus
                    onKeyDown={(e) => { if (e.key === 'Enter') handleSave(); if (e.key === 'Escape') { setFullName(profile.fullName); setIsEditingName(false) } }}
                    className="rounded-lg border border-gray-300 px-3 py-1.5 text-xl font-bold text-gray-900 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                  />
                  <button onClick={handleSave} disabled={updateProfile.isPending || fullName.trim() === profile.fullName} className="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors">
                    {updateProfile.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : 'Guardar'}
                  </button>
                  <button onClick={() => { setFullName(profile.fullName); setIsEditingName(false) }} className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">
                    Cancelar
                  </button>
                </div>
              ) : (
                <button onClick={() => setIsEditingName(true)} className="group flex items-center gap-2">
                  <h1 className="text-2xl font-bold text-gray-900">{profile.fullName}</h1>
                  <Pencil className="h-4 w-4 text-gray-400 opacity-0 transition-opacity group-hover:opacity-100" />
                </button>
              )}
              {updateProfile.isSuccess && <p className="mt-0.5 text-xs text-green-600">Nombre actualizado.</p>}
              <div className="mt-3 flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center gap-1.5 rounded-full bg-blue-100 px-3 py-1 text-xs font-semibold text-blue-700">
                  <Shield className="h-3 w-3" />
                  {profile.role}
                </span>
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
                <button type="button" onClick={() => fileInputRef.current?.click()} className="flex items-center gap-1.5 rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 shadow-sm hover:bg-gray-50">
                  <Upload className="h-3.5 w-3.5" /> Subir foto
                </button>
                <button type="button" onClick={() => cameraInputRef.current?.click()} className="flex items-center gap-1.5 rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 shadow-sm hover:bg-gray-50">
                  <Camera className="h-3.5 w-3.5" /> Tomar foto
                </button>
                {hasAvatar && (
                  <button type="button" onClick={() => deleteAvatar.mutate()} disabled={deleteAvatar.isPending} className="flex items-center gap-1.5 rounded-lg border border-red-200 bg-white px-3 py-1.5 text-xs font-medium text-red-600 shadow-sm hover:bg-red-50">
                    <Trash2 className="h-3.5 w-3.5" /> Eliminar
                  </button>
                )}
              </div>
            </div>
          </div>

          <input ref={fileInputRef} type="file" accept="image/*" className="hidden" onChange={handleFileSelect} />
          <input ref={cameraInputRef} type="file" accept="image/*" capture="user" className="hidden" onChange={handleFileSelect} />

          {/* Two column grid */}
          <div className="mt-8 grid grid-cols-1 gap-10 lg:grid-cols-2">
            {/* Left: Info */}
            <div className="rounded-2xl bg-white p-8 shadow-sm">
              <div className="mb-6 flex items-center gap-3">
                <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-blue-100">
                  <User className="h-5 w-5 text-blue-600" />
                </div>
                <h2 className="text-lg font-semibold text-gray-900">Informacion personal</h2>
              </div>

              <div className="space-y-5">
                <div className="flex items-start gap-4 rounded-xl bg-gray-50 p-5">
                  <div className="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-xl bg-blue-100">
                    <Mail className="h-6 w-6 text-blue-600" />
                  </div>
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-gray-500">Correo electronico</p>
                    <p className="mt-1 truncate text-base font-semibold text-gray-900">{profile.email}</p>
                  </div>
                </div>

                <div className="flex items-start gap-4 rounded-xl bg-gray-50 p-5">
                  <div className="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-xl bg-emerald-100">
                    <Shield className="h-6 w-6 text-emerald-600" />
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-500">Rol en el sistema</p>
                    <p className="mt-1 text-base font-semibold text-gray-900">{profile.role}</p>
                  </div>
                </div>

                <div className="flex items-start gap-4 rounded-xl bg-gray-50 p-5">
                  <div className="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-xl bg-purple-100">
                    <Calendar className="h-6 w-6 text-purple-600" />
                  </div>
                  <div>
                    <p className="text-sm font-medium text-gray-500">Miembro desde</p>
                    <p className="mt-1 text-base font-semibold text-gray-900">
                      {profile.createdAt ? new Date(profile.createdAt).toLocaleDateString('es-PA', { year: 'numeric', month: 'long', day: 'numeric' }) : '—'}
                    </p>
                  </div>
                </div>

                {/* Teléfono de notificación WhatsApp */}
                <div className="flex items-start gap-4 rounded-xl bg-gray-50 p-5">
                  <div className="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-xl bg-green-100">
                    <Phone className="h-6 w-6 text-green-600" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-gray-500">WhatsApp de notificaciones</p>
                    <p className="mt-0.5 text-xs text-gray-400">Se usa para recibir alertas de Transfer Chat cuando un cliente solicita atención humana.</p>
                    {isEditingPhone ? (
                      <div className="mt-2 flex items-center gap-2">
                        <input
                          type="tel"
                          value={notifyPhone}
                          onChange={(e) => setNotifyPhone(e.target.value)}
                          autoFocus
                          placeholder="+50768001234"
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') handleSavePhone()
                            if (e.key === 'Escape') { setNotifyPhone(profile.notifyPhone ?? ''); setIsEditingPhone(false) }
                          }}
                          className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-semibold text-gray-900 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                        />
                        <button onClick={handleSavePhone} disabled={updateProfile.isPending} className="rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors">
                          {updateProfile.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : 'Guardar'}
                        </button>
                        <button onClick={() => { setNotifyPhone(profile.notifyPhone ?? ''); setIsEditingPhone(false) }} className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">
                          Cancelar
                        </button>
                      </div>
                    ) : (
                      <button onClick={() => setIsEditingPhone(true)} className="group mt-1 flex items-center gap-2">
                        <p className="text-base font-semibold text-gray-900">{profile.notifyPhone || '—'}</p>
                        <Pencil className="h-3.5 w-3.5 text-gray-400 opacity-0 transition-opacity group-hover:opacity-100" />
                      </button>
                    )}
                  </div>
                </div>
              </div>
            </div>

            {/* Right: Change Password */}
            <div className="rounded-2xl bg-white p-8 shadow-sm">
              <div className="mb-6 flex items-center gap-3">
                <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-amber-100">
                  <KeyRound className="h-5 w-5 text-amber-600" />
                </div>
                <h2 className="text-lg font-semibold text-gray-900">Cambiar contrasena</h2>
              </div>

              {pwError && <div className="mb-4 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-600">{pwError}</div>}
              {pwSuccess && <div className="mb-4 rounded-lg bg-green-50 px-4 py-3 text-sm text-green-600">Contrasena actualizada correctamente.</div>}

              <div className="space-y-5">
                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-600">Contrasena actual</label>
                  <div className="relative">
                    <input
                      type={showCurrentPw ? 'text' : 'password'}
                      value={currentPassword}
                      onChange={e => setCurrentPassword(e.target.value)}
                      className="block w-full rounded-xl border border-gray-300 px-4 py-2.5 pr-10 text-sm transition-colors focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                      placeholder="Ingresa tu contrasena actual"
                    />
                    <button type="button" onClick={() => setShowCurrentPw(!showCurrentPw)} className="absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600">
                      {showCurrentPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-600">Nueva contrasena</label>
                  <div className="relative">
                    <input
                      type={showNewPw ? 'text' : 'password'}
                      value={newPassword}
                      onChange={e => setNewPassword(e.target.value)}
                      className="block w-full rounded-xl border border-gray-300 px-4 py-2.5 pr-10 text-sm transition-colors focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                      placeholder="Minimo 8 caracteres"
                    />
                    <button type="button" onClick={() => setShowNewPw(!showNewPw)} className="absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600">
                      {showNewPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                  {newPassword && (() => { const s = getStrength(newPassword); return (
                    <div className="mt-2">
                      <div className="h-1.5 w-full rounded-full bg-gray-200"><div className={`h-1.5 rounded-full transition-all ${s.color} ${s.width}`} /></div>
                      <p className={`mt-1 text-xs ${s.text}`}>{s.label}</p>
                    </div>
                  )})()}
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-gray-600">Confirmar nueva contrasena</label>
                  <input
                    type="password"
                    value={confirmPassword}
                    onChange={e => setConfirmPassword(e.target.value)}
                    className="block w-full rounded-xl border border-gray-300 px-4 py-2.5 text-sm transition-colors focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                    placeholder="Repite la nueva contrasena"
                  />
                </div>

                <button
                  type="button"
                  onClick={handleChangePassword}
                  disabled={changePassword.isPending}
                  className="mt-2 flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-blue-600 to-indigo-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition-all hover:from-blue-700 hover:to-indigo-700 disabled:opacity-50"
                >
                  {changePassword.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <KeyRound className="h-4 w-4" />}
                  {changePassword.isPending ? 'Cambiando...' : 'Cambiar contrasena'}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
