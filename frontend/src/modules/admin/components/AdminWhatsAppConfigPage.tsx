import { useMemo, useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import {
  Phone, Plus, RefreshCw, LogOut, QrCode, Trash2, Pencil, X, Loader2, Send, Building2, Search,
} from 'lucide-react'
import { toast, confirmDialog } from '@/shared/components/dialog'
import { useAdminTenants } from '@/modules/admin/hooks/useAdminTenants'
import {
  useAdminLineStatus, useAdminLineQr, useAdminRestartLine, useAdminLogoutLine,
} from '@/modules/admin/hooks/useAdminWhatsAppLines'
import {
  useAdminWaConfigList, useCreateWaConfig, useUpdateWaConfig, useDeleteWaConfig, useWaConfigTestMessage,
  type AdminWaConfigLine,
} from '@/modules/admin/hooks/useAdminWhatsAppConfig'

// ── Schemas (espejo del tab del cliente) ──────────────────────────
const baseFields = {
  provider: z.enum(['UltraMsg', 'MetaCloudApi']).default('UltraMsg'),
  displayName: z.string().min(2, 'Nombre requerido'),
  phoneNumber: z.string().optional().default(''),
  instanceId: z.string().min(3, 'Requerido'),
  apiToken: z.string().optional().default(''),
  metaWabaId: z.string().optional().default(''),
  metaAccessToken: z.string().optional().default(''),
  metaAppSecret: z.string().optional().default(''),
  metaBusinessId: z.string().optional().default(''),
}

const createSchema = z.object({
  ...baseFields,
  tenantId: z.string().min(1, 'Selecciona un tenant'),
}).superRefine((d, ctx) => {
  if (d.provider === 'UltraMsg') {
    if (!d.apiToken || d.apiToken.length < 3)
      ctx.addIssue({ path: ['apiToken'], code: z.ZodIssueCode.custom, message: 'Token requerido' })
  } else {
    if (!d.metaWabaId) ctx.addIssue({ path: ['metaWabaId'], code: z.ZodIssueCode.custom, message: 'WABA ID requerido' })
    if (!d.metaAccessToken) ctx.addIssue({ path: ['metaAccessToken'], code: z.ZodIssueCode.custom, message: 'Access Token requerido' })
  }
})
type CreateForm = z.infer<typeof createSchema>

const editSchema = z.object({ ...baseFields, isActive: z.boolean() })
type EditForm = z.infer<typeof editSchema>

// ── Semáforo ──────────────────────────────────────
const semaphoreConfig: Record<string, { color: string; pulse?: boolean; label: string }> = {
  authenticated: { color: 'bg-green-500', label: 'Conectado' },
  qr: { color: 'bg-yellow-400', pulse: true, label: 'Esperando QR' },
  loading: { color: 'bg-yellow-400', pulse: true, label: 'Cargando' },
  initialize: { color: 'bg-blue-500', pulse: true, label: 'Inicializando' },
  disconnected: { color: 'bg-red-500', label: 'Desconectado' },
  standby: { color: 'bg-gray-400', label: 'En espera' },
  unknown: { color: 'bg-gray-400', label: 'Desconocido' },
}
const getSemaphore = (s: string) => semaphoreConfig[s] ?? { color: 'bg-gray-400', label: s }

const isMetaP = (p: string) => p === 'MetaCloudApi'

// ── Fila de línea ─────────────────────────────────
function LineRow({
  line, onEdit, onDelete, onShowQr, onTest,
}: {
  line: AdminWaConfigLine
  onEdit: (l: AdminWaConfigLine) => void
  onDelete: (l: AdminWaConfigLine) => void
  onShowQr: (id: string) => void
  onTest: (id: string) => void
}) {
  const isMeta = isMetaP(line.provider)
  const { data: statusData, isError } = useAdminLineStatus(line.id, !isMeta)
  const restartMutation = useAdminRestartLine()
  const logoutMutation = useAdminLogoutLine()

  const currentStatus = statusData?.status ?? 'loading'
  const sem = isMeta
    ? { color: 'bg-blue-500', pulse: false, label: 'Meta Cloud API' }
    : getSemaphore(isError ? 'disconnected' : currentStatus)
  const isConnected = !isMeta && currentStatus === 'authenticated'
  const needsQr = !isMeta && (!isConnected || isError)

  const handleRestart = async () => {
    const ok = await confirmDialog({
      title: 'Reiniciar instancia',
      description: `Esto reiniciará la instancia "${line.displayName}". La conexión se interrumpirá brevemente.`,
      confirmLabel: 'Reiniciar',
    })
    if (ok) restartMutation.mutate(line.id)
  }
  const handleLogout = async () => {
    const ok = await confirmDialog({
      title: 'Cerrar sesión de WhatsApp',
      description: `Se cerrará la sesión de "${line.displayName}" y necesitarás escanear un nuevo QR.`,
      variant: 'danger',
      confirmLabel: 'Cerrar sesión',
    })
    if (ok) logoutMutation.mutate(line.id)
  }

  return (
    <div className="flex items-center justify-between border-b border-gray-100 px-4 py-3 last:border-b-0">
      <div className="flex min-w-0 items-center gap-3">
        <span
          className={`inline-block h-3 w-3 shrink-0 rounded-full ${sem.color} ${sem.pulse ? 'animate-pulse' : ''}`}
          title={sem.label}
        />
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <p className="truncate text-sm font-semibold text-gray-900">{line.displayName}</p>
            <span className={`rounded-full px-2 py-0.5 text-[10px] font-medium ${isMeta ? 'bg-blue-50 text-blue-700' : 'bg-emerald-50 text-emerald-700'}`}>
              {isMeta ? 'Meta' : 'UltraMsg'}
            </span>
            {!line.isActive && (
              <span className="rounded-full bg-gray-100 px-2 py-0.5 text-[10px] text-gray-500">Inactivo</span>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-3 text-xs text-gray-500">
            {(statusData?.phone || line.phoneNumber) && (
              <span className="flex items-center gap-1">
                <Phone className="h-3 w-3" />{statusData?.phone || line.phoneNumber}
              </span>
            )}
            <span className="text-gray-400">{isMeta ? 'Phone ID' : 'Instancia'}: {line.instanceId}</span>
            <span className={isConnected ? 'font-medium text-green-600' : isError ? 'text-red-500' : 'text-gray-400'}>
              {sem.label}
            </span>
          </div>
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-1">
        {needsQr && (
          <button onClick={() => onShowQr(line.id)} className="flex items-center gap-1 rounded-lg bg-amber-500 px-2.5 py-1.5 text-xs font-medium text-gray-900 transition-colors hover:bg-amber-400" title="Vincular por QR">
            <QrCode className="h-3.5 w-3.5" /> Vincular
          </button>
        )}
        {(isConnected || isMeta) && (
          <button onClick={() => onTest(line.id)} className="flex items-center gap-1 rounded-lg bg-green-600 px-2.5 py-1.5 text-xs font-medium text-white transition-colors hover:bg-green-700" title="Enviar mensaje de prueba">
            <Send className="h-3.5 w-3.5" /> Probar
          </button>
        )}
        {!isMeta && (
          <button onClick={handleRestart} disabled={restartMutation.isPending} className="rounded-lg p-1.5 text-gray-400 transition-colors hover:bg-gray-100 hover:text-blue-600 disabled:opacity-50" title="Reiniciar">
            <RefreshCw className={`h-4 w-4 ${restartMutation.isPending ? 'animate-spin' : ''}`} />
          </button>
        )}
        {isConnected && (
          <button onClick={handleLogout} disabled={logoutMutation.isPending} className="rounded-lg p-1.5 text-gray-400 transition-colors hover:bg-gray-100 hover:text-red-600 disabled:opacity-50" title="Cerrar sesión">
            <LogOut className="h-4 w-4" />
          </button>
        )}
        <button onClick={() => onEdit(line)} className="rounded-lg p-1.5 text-gray-400 transition-colors hover:bg-gray-100 hover:text-blue-600" title="Editar">
          <Pencil className="h-4 w-4" />
        </button>
        <button onClick={() => onDelete(line)} className="rounded-lg p-1.5 text-gray-400 transition-colors hover:bg-gray-100 hover:text-red-600" title="Eliminar">
          <Trash2 className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}

// ── QR Modal (reusa endpoints admin por línea) ────
function QrModal({ lineId, onClose }: { lineId: string; onClose: () => void }) {
  const { data: qrUrl, isLoading, isError } = useAdminLineQr(lineId)
  const { data: statusData } = useAdminLineStatus(lineId)
  if (statusData?.status === 'authenticated' && qrUrl) setTimeout(onClose, 1500)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
        <div className="mb-4 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-gray-900">Vincular WhatsApp</h3>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:text-gray-600"><X className="h-5 w-5" /></button>
        </div>
        <div className="flex flex-col items-center py-4">
          {isLoading ? (
            <div className="flex h-64 w-64 items-center justify-center"><Loader2 className="h-8 w-8 animate-spin text-gray-400" /></div>
          ) : isError ? (
            <div className="flex h-64 w-64 flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300">
              <QrCode className="h-16 w-16 text-gray-300" />
              <p className="mt-2 text-xs text-gray-400">No se pudo obtener el QR. Reinicia la instancia.</p>
            </div>
          ) : qrUrl ? (
            <img src={qrUrl} alt="QR Code" className="h-64 w-64 rounded-lg border border-gray-200 p-2" />
          ) : (
            <div className="flex h-64 w-64 flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300">
              <QrCode className="h-16 w-16 text-gray-300" />
              <p className="mt-2 text-center text-xs text-gray-400">
                {statusData?.status === 'authenticated' ? 'La instancia ya está conectada' : 'QR no disponible. Reinicia e intenta de nuevo.'}
              </p>
            </div>
          )}
          {statusData?.status === 'authenticated' ? (
            <div className="mt-4 flex items-center gap-2 text-green-600">
              <span className="inline-block h-3 w-3 rounded-full bg-green-500" />
              <p className="text-sm font-medium">Conectado exitosamente</p>
            </div>
          ) : (
            <div className="mt-4 text-center">
              <p className="text-sm font-medium text-gray-900">Escanea con WhatsApp</p>
              <p className="mt-1 text-xs text-gray-500">Menú &rarr; Dispositivos vinculados &rarr; Vincular dispositivo</p>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Test Message Modal ────────────────────────────
function TestMessageModal({ lineId, onClose }: { lineId: string; onClose: () => void }) {
  const sendTest = useWaConfigTestMessage()
  const [to, setTo] = useState('')
  const [message, setMessage] = useState('Hola, este es un mensaje de prueba desde AgentFlow.')

  const handleSend = () => {
    if (!to.trim()) return
    sendTest.mutate({ id: lineId, to: to.trim(), message }, {
      onSuccess: () => { toast.success('Mensaje de prueba enviado.'); setTimeout(onClose, 1500) },
      onError: () => toast.error('No se pudo enviar. Verifica el número y la conexión.'),
    })
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div className="w-full max-w-md rounded-lg bg-white p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
        <div className="mb-4 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-gray-900">Enviar mensaje de prueba</h3>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:text-gray-600"><X className="h-5 w-5" /></button>
        </div>
        <div className="space-y-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-700">Número destino (con código de país)</label>
            <input type="text" value={to} onChange={(e) => setTo(e.target.value)} placeholder="+50760001234" className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-700">Mensaje</label>
            <textarea value={message} onChange={(e) => setMessage(e.target.value)} rows={3} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500" />
          </div>
          <button onClick={handleSend} disabled={sendTest.isPending || !to.trim()} className="flex w-full items-center justify-center gap-2 rounded-lg bg-green-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-green-700 disabled:opacity-50">
            {sendTest.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
            {sendTest.isPending ? 'Enviando...' : 'Enviar mensaje de prueba'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Campos del formulario (compartidos crear/editar) ──
function ProviderFields({ form, provider, editLine }: {
  // react-hook-form's UseFormReturn type difiere entre crear/editar; usamos any para
  // compartir el render de campos sin duplicar. Validación real la hace Zod.
  form: any
  provider: string
  editLine?: AdminWaConfigLine | null
}) {
  const meta = isMetaP(provider)
  return (
    <div className="grid grid-cols-2 gap-3">
      <div>
        <input placeholder="Nombre (ej: Cobros Principal)" {...form.register('displayName')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
        {form.formState.errors.displayName && <p className="mt-1 text-xs text-red-600">{form.formState.errors.displayName.message}</p>}
      </div>
      <input placeholder="Teléfono (ej: +50760001234)" {...form.register('phoneNumber')} className="rounded-md border border-gray-300 px-3 py-2 text-sm" />
      <div>
        <input placeholder={meta ? 'Phone Number ID' : 'ID de Instancia (ej: 140984)'} {...form.register('instanceId')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
        {form.formState.errors.instanceId && <p className="mt-1 text-xs text-red-600">{form.formState.errors.instanceId.message}</p>}
      </div>
      {meta ? (
        <>
          <div>
            <input placeholder="WABA ID" {...form.register('metaWabaId')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
            {form.formState.errors.metaWabaId && <p className="mt-1 text-xs text-red-600">{form.formState.errors.metaWabaId.message}</p>}
          </div>
          <div>
            <input type="password" placeholder={editLine?.metaAccessTokenLast4 ? `•••• ${editLine.metaAccessTokenLast4} — vacío = no cambiar` : 'Access Token (Bearer)'} {...form.register('metaAccessToken')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
            {form.formState.errors.metaAccessToken && <p className="mt-1 text-xs text-red-600">{form.formState.errors.metaAccessToken.message}</p>}
          </div>
          <input type="password" placeholder={editLine?.metaAppSecretLast4 ? `•••• ${editLine.metaAppSecretLast4} — vacío = no cambiar` : 'App Secret'} {...form.register('metaAppSecret')} className="rounded-md border border-gray-300 px-3 py-2 text-sm" />
          <input placeholder="Business ID (opcional)" {...form.register('metaBusinessId')} className="rounded-md border border-gray-300 px-3 py-2 text-sm" />
        </>
      ) : (
        <div>
          <input type="password" placeholder={editLine ? 'Token (vacío = no cambiar)' : 'Token de UltraMsg'} {...form.register('apiToken')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
          {form.formState.errors.apiToken && <p className="mt-1 text-xs text-red-600">{form.formState.errors.apiToken.message}</p>}
        </div>
      )}
    </div>
  )
}

// ── Modal genérico para crear/editar (visible sin importar el scroll) ──
function FormModal({ title, accent, onClose, children }: {
  title: ReactNode
  accent: 'green' | 'amber'
  onClose: () => void
  children: ReactNode
}) {
  const ring = accent === 'green' ? 'border-green-200' : 'border-amber-200'
  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/50 p-4 sm:items-center" onClick={onClose}>
      <div className={`my-8 w-full max-w-2xl rounded-lg border ${ring} bg-white shadow-xl`} onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-gray-100 px-5 py-3">
          <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:text-gray-600"><X className="h-5 w-5" /></button>
        </div>
        <div className="max-h-[75vh] overflow-y-auto p-5">{children}</div>
      </div>
    </div>
  )
}

// ── Página principal ──────────────────────────────
export function AdminWhatsAppConfigPage() {
  const { data: lines, isLoading } = useAdminWaConfigList()
  const { data: tenants } = useAdminTenants()
  const createMut = useCreateWaConfig()
  const updateMut = useUpdateWaConfig()
  const deleteMut = useDeleteWaConfig()

  const [showForm, setShowForm] = useState(false)
  const [editLine, setEditLine] = useState<AdminWaConfigLine | null>(null)
  const [qrLineId, setQrLineId] = useState<string | null>(null)
  const [testLineId, setTestLineId] = useState<string | null>(null)
  const [filterInstance, setFilterInstance] = useState('')
  const [filterTenant, setFilterTenant] = useState('')

  const createForm = useForm<CreateForm>({
    resolver: zodResolver(createSchema),
    defaultValues: {
      provider: 'UltraMsg', tenantId: '', displayName: '', phoneNumber: '', instanceId: '',
      apiToken: '', metaWabaId: '', metaAccessToken: '', metaAppSecret: '', metaBusinessId: '',
    },
  })
  const createProvider = createForm.watch('provider')

  const editForm = useForm<EditForm>({ resolver: zodResolver(editSchema) })
  const editProvider = editForm.watch('provider')

  // Agrupar líneas por tenant (null = "Sin tenant / pruebas"), ordenadas por nombre.
  // Filtros por Instance ID y nombre de tenant (substring, case-insensitive).
  const groups = useMemo(() => {
    const fi = filterInstance.trim().toLowerCase()
    const ft = filterTenant.trim().toLowerCase()
    const map = new Map<string, { key: string; name: string; lines: AdminWaConfigLine[] }>()
    for (const l of lines ?? []) {
      const name = l.tenantName ?? 'Sin tenant / pruebas'
      if (fi && !l.instanceId.toLowerCase().includes(fi)) continue
      if (ft && !name.toLowerCase().includes(ft)) continue
      const key = l.tenantId ?? '__none__'
      if (!map.has(key)) map.set(key, { key, name, lines: [] })
      map.get(key)!.lines.push(l)
    }
    return [...map.values()].sort((a, b) => a.name.localeCompare(b.name))
  }, [lines, filterInstance, filterTenant])

  const totalShown = useMemo(() => groups.reduce((n, g) => n + g.lines.length, 0), [groups])
  const hasFilter = !!(filterInstance.trim() || filterTenant.trim())

  const onCreateSubmit = async (data: CreateForm) => {
    const meta = isMetaP(data.provider)
    try {
      await createMut.mutateAsync({
        tenantId: data.tenantId || null,
        provider: data.provider,
        displayName: data.displayName,
        phoneNumber: data.phoneNumber ?? '',
        instanceId: data.instanceId,
        apiToken: meta ? undefined : data.apiToken,
        metaWabaId: meta ? data.metaWabaId : undefined,
        metaAccessToken: meta ? data.metaAccessToken : undefined,
        metaAppSecret: meta ? (data.metaAppSecret || undefined) : undefined,
        metaBusinessId: meta ? (data.metaBusinessId || undefined) : undefined,
      })
      createForm.reset()
      setShowForm(false)
      toast.success('Línea creada.')
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      createForm.setError('instanceId', { message: axiosErr.response?.data?.error ?? 'Error al crear' })
    }
  }

  const openEdit = (line: AdminWaConfigLine) => {
    setEditLine(line)
    setShowForm(false)
    editForm.reset({
      provider: (line.provider as 'UltraMsg' | 'MetaCloudApi'),
      displayName: line.displayName,
      phoneNumber: line.phoneNumber,
      instanceId: line.instanceId,
      apiToken: '',
      metaWabaId: line.metaWabaId ?? '',
      metaAccessToken: '',
      metaAppSecret: '',
      metaBusinessId: line.metaBusinessId ?? '',
      isActive: line.isActive,
    })
  }

  const onEditSubmit = async (data: EditForm) => {
    if (!editLine) return
    const meta = isMetaP(data.provider)
    try {
      await updateMut.mutateAsync({
        id: editLine.id,
        provider: data.provider,
        displayName: data.displayName,
        phoneNumber: data.phoneNumber ?? '',
        instanceId: data.instanceId,
        apiToken: meta ? undefined : (data.apiToken || undefined),
        metaWabaId: meta ? (data.metaWabaId || undefined) : undefined,
        metaAccessToken: meta ? (data.metaAccessToken || undefined) : undefined,
        metaAppSecret: meta ? (data.metaAppSecret || undefined) : undefined,
        metaBusinessId: meta ? (data.metaBusinessId || undefined) : undefined,
        isActive: data.isActive,
      })
      setEditLine(null)
      toast.success('Línea actualizada.')
    } catch {
      editForm.setError('displayName', { message: 'Error al actualizar' })
    }
  }

  const handleDelete = async (line: AdminWaConfigLine) => {
    const ok = await confirmDialog({
      title: 'Eliminar línea',
      description: `Se eliminará la línea "${line.displayName}"${line.tenantName ? ` del tenant "${line.tenantName}"` : ''}. Los agentes que la usen dejarán de funcionar.`,
      variant: 'danger',
      confirmLabel: 'Eliminar',
    })
    if (!ok) return
    try {
      await deleteMut.mutateAsync(line.id)
      toast.success('Línea eliminada.')
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      toast.error(axiosErr.response?.data?.error ?? 'No se pudo eliminar la línea.')
    }
  }

  return (
    <div>
      {/* Header */}
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Configuración WhatsApp por tenant</h1>
          <p className="text-sm text-gray-500">Todas las líneas de WhatsApp de todos los corredores, agrupadas por tenant.</p>
        </div>
        <button onClick={() => { setShowForm((v) => !v); setEditLine(null) }} className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 transition-colors hover:bg-amber-400">
          <Plus className="h-4 w-4" /> Configurar nueva
        </button>
      </div>

      {/* Filtros */}
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
          <input
            value={filterInstance}
            onChange={(e) => setFilterInstance(e.target.value)}
            placeholder="Filtrar por Instance ID / Phone ID"
            className="w-64 rounded-md border border-gray-300 py-2 pl-8 pr-3 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
          />
        </div>
        <div className="relative">
          <Building2 className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
          <input
            value={filterTenant}
            onChange={(e) => setFilterTenant(e.target.value)}
            placeholder="Filtrar por tenant"
            className="w-56 rounded-md border border-gray-300 py-2 pl-8 pr-3 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
          />
        </div>
        {hasFilter && (
          <>
            <button
              onClick={() => { setFilterInstance(''); setFilterTenant('') }}
              className="flex items-center gap-1 rounded-md border border-gray-300 px-3 py-2 text-xs font-medium text-gray-600 transition-colors hover:bg-gray-50"
            >
              <X className="h-3.5 w-3.5" /> Limpiar
            </button>
            <span className="text-xs text-gray-500">{totalShown} {totalShown === 1 ? 'línea' : 'líneas'}</span>
          </>
        )}
      </div>

      {/* Crear (modal) */}
      {showForm && (
        <FormModal title="Nueva línea de WhatsApp" accent="green" onClose={() => setShowForm(false)}>
          <form onSubmit={createForm.handleSubmit(onCreateSubmit)}>
            <div className="mb-3 grid grid-cols-2 gap-3">
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Tenant</label>
                <select {...createForm.register('tenantId')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm">
                  <option value="">Selecciona un tenant…</option>
                  {tenants?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                </select>
                {createForm.formState.errors.tenantId && <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.tenantId.message}</p>}
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-gray-600">Proveedor</label>
                <select {...createForm.register('provider')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm">
                  <option value="UltraMsg">UltraMsg (vinculación por QR)</option>
                  <option value="MetaCloudApi">Meta WhatsApp Cloud API (oficial)</option>
                </select>
              </div>
            </div>
            <ProviderFields form={createForm} provider={createProvider} />
            <div className="mt-4 flex items-center gap-2">
              <button type="submit" disabled={createMut.isPending} className="flex items-center gap-1.5 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 transition-colors hover:bg-amber-400 disabled:opacity-50">
                {createMut.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />} Crear línea
              </button>
              <button type="button" onClick={() => setShowForm(false)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50">Cancelar</button>
            </div>
          </form>
        </FormModal>
      )}

      {/* Editar (modal) */}
      {editLine && (
        <FormModal
          accent="amber"
          onClose={() => setEditLine(null)}
          title={<>Editar: {editLine.displayName}{editLine.tenantName && <span className="ml-2 text-xs font-normal text-gray-500">({editLine.tenantName})</span>}</>}
        >
          <form onSubmit={editForm.handleSubmit(onEditSubmit)}>
            <div className="mb-3">
              <label className="mb-1 block text-xs font-medium text-gray-600">Proveedor</label>
              <select {...editForm.register('provider')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm">
                <option value="UltraMsg">UltraMsg (vinculación por QR)</option>
                <option value="MetaCloudApi">Meta WhatsApp Cloud API (oficial)</option>
              </select>
            </div>
            <ProviderFields form={editForm} provider={editProvider} editLine={editLine} />
            <div className="mt-3 flex items-center gap-3">
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input type="checkbox" {...editForm.register('isActive')} className="rounded border-gray-300" /> Activa
              </label>
            </div>
            <div className="mt-4 flex items-center gap-2">
              <button type="submit" disabled={updateMut.isPending} className="flex items-center gap-1.5 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 transition-colors hover:bg-amber-400 disabled:opacity-50">
                {updateMut.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />} Actualizar
              </button>
              <button type="button" onClick={() => setEditLine(null)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50">Cancelar</button>
            </div>
          </form>
        </FormModal>
      )}

      {/* Grid agrupado por tenant */}
      {isLoading ? (
        <div className="py-12 text-center text-gray-400">
          <Loader2 className="mx-auto h-8 w-8 animate-spin text-gray-300" />
          <p className="mt-2 text-sm">Cargando configuraciones...</p>
        </div>
      ) : !groups.length ? (
        <div className="py-16 text-center">
          <Phone className="mx-auto h-12 w-12 text-gray-300" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">
            {hasFilter ? 'Sin resultados' : 'Sin líneas de WhatsApp'}
          </h3>
          <p className="mt-1 text-sm text-gray-500">
            {hasFilter ? 'Ninguna línea coincide con el filtro. Probá limpiarlo.' : 'Configura una nueva línea para un tenant.'}
          </p>
        </div>
      ) : (
        <div className="space-y-5">
          {groups.map((g) => (
            <div key={g.key} className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
              <div className="flex items-center justify-between border-b border-gray-200 bg-gray-50 px-4 py-2.5">
                <div className="flex items-center gap-2">
                  <Building2 className="h-4 w-4 text-gray-400" />
                  <span className="text-sm font-semibold text-gray-900">{g.name}</span>
                </div>
                <span className="rounded-full bg-gray-200 px-2 py-0.5 text-xs font-medium text-gray-600">
                  {g.lines.length} {g.lines.length === 1 ? 'línea' : 'líneas'}
                </span>
              </div>
              <div>
                {g.lines.map((line) => (
                  <LineRow key={line.id} line={line} onEdit={openEdit} onDelete={handleDelete} onShowQr={setQrLineId} onTest={setTestLineId} />
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      {qrLineId && <QrModal lineId={qrLineId} onClose={() => setQrLineId(null)} />}
      {testLineId && <TestMessageModal lineId={testLineId} onClose={() => setTestLineId(null)} />}
    </div>
  )
}
