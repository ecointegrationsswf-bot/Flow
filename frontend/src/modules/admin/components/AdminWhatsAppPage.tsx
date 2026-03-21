import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import {
  Phone, Plus, RefreshCw, LogOut, QrCode, Trash2, Pencil, X, Loader2,
} from 'lucide-react'
import {
  useAdminWhatsAppLines,
  useAdminCreateWhatsAppLine,
  useAdminUpdateWhatsAppLine,
  useAdminDeleteWhatsAppLine,
  useAdminLineStatus,
  useAdminLineQr,
  useAdminRestartLine,
  useAdminLogoutLine,
} from '@/modules/admin/hooks/useAdminWhatsAppLines'
import type { AdminWhatsAppLine } from '@/modules/admin/hooks/useAdminWhatsAppLines'

// ── Schemas ───────────────────────────────────────
const createSchema = z.object({
  displayName: z.string().min(2, 'Nombre requerido'),
  phoneNumber: z.string().optional().default(''),
  instanceId: z.string().min(3, 'Instance ID requerido'),
  apiToken: z.string().min(3, 'Token requerido'),
})
type CreateForm = z.infer<typeof createSchema>

const editSchema = z.object({
  displayName: z.string().min(2, 'Nombre requerido'),
  phoneNumber: z.string().optional().default(''),
  instanceId: z.string().min(3, 'Instance ID requerido'),
  apiToken: z.string().optional().default(''),
  isActive: z.boolean(),
})
type EditForm = z.infer<typeof editSchema>

// ── Semaphore config ──────────────────────────────
const semaphoreConfig: Record<string, { color: string; pulse?: boolean; label: string }> = {
  authenticated: { color: 'bg-green-500', label: 'Conectado' },
  qr: { color: 'bg-yellow-400', pulse: true, label: 'Esperando QR' },
  loading: { color: 'bg-yellow-400', pulse: true, label: 'Cargando' },
  initialize: { color: 'bg-blue-500', pulse: true, label: 'Inicializando' },
  disconnected: { color: 'bg-red-500', label: 'Desconectado' },
  standby: { color: 'bg-gray-400', label: 'En espera' },
  unknown: { color: 'bg-gray-400', label: 'Desconocido' },
}

function getSemaphore(status: string) {
  return semaphoreConfig[status] ?? { color: 'bg-gray-400', label: status }
}

// ── Line Card ─────────────────────────────────────
function LineCard({
  line,
  onEdit,
  onDelete,
  onShowQr,
}: {
  line: AdminWhatsAppLine
  onEdit: (line: AdminWhatsAppLine) => void
  onDelete: (line: AdminWhatsAppLine) => void
  onShowQr: (lineId: string) => void
}) {
  const { data: statusData, isError } = useAdminLineStatus(line.id)
  const restartMutation = useAdminRestartLine()
  const logoutMutation = useAdminLogoutLine()

  const currentStatus = statusData?.status ?? 'loading'
  const sem = getSemaphore(isError ? 'disconnected' : currentStatus)
  const isConnected = currentStatus === 'authenticated'
  const needsQr = !isConnected || isError

  const [confirmRestart, setConfirmRestart] = useState(false)
  const [confirmLogout, setConfirmLogout] = useState(false)

  return (
    <div className="flex items-center justify-between rounded-lg border border-gray-200 bg-white px-5 py-4 shadow-sm">
      <div className="flex items-center gap-3 min-w-0">
        {/* Semaforo */}
        <span
          className={`inline-block h-3 w-3 shrink-0 rounded-full ${sem.color} ${sem.pulse ? 'animate-pulse' : ''}`}
          title={sem.label}
        />

        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <p className="truncate text-sm font-semibold text-gray-900">{line.displayName}</p>
            {!line.isActive && (
              <span className="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">Inactivo</span>
            )}
          </div>
          <div className="flex items-center gap-3 text-xs text-gray-500">
            {(statusData?.phone || line.phoneNumber) && (
              <span className="flex items-center gap-1">
                <Phone className="h-3 w-3" />
                {statusData?.phone || line.phoneNumber}
              </span>
            )}
            <span className="text-gray-400">Instancia: {line.instanceId}</span>
            <span className={isConnected ? 'text-green-600 font-medium' : isError ? 'text-red-500' : 'text-gray-400'}>
              {sem.label}
            </span>
          </div>
        </div>
      </div>

      <div className="flex items-center gap-1 shrink-0">
        {/* Boton QR - siempre visible cuando no esta conectado */}
        {needsQr && (
          <button
            onClick={() => onShowQr(line.id)}
            className="flex items-center gap-1 rounded-lg bg-amber-500 px-2.5 py-1.5 text-xs font-medium text-gray-900 hover:bg-amber-400 transition-colors"
            title="Vincular por QR"
          >
            <QrCode className="h-3.5 w-3.5" />
            Vincular
          </button>
        )}
        {/* Siempre visible: boton QR como icono para ver/reescanear */}
        {isConnected && (
          <button
            onClick={() => onShowQr(line.id)}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
            title="Ver QR / Reescanear"
          >
            <QrCode className="h-4 w-4" />
          </button>
        )}
        <button
          onClick={() => setConfirmRestart(true)}
          disabled={restartMutation.isPending}
          className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 disabled:opacity-50 transition-colors"
          title="Reiniciar instancia"
        >
          <RefreshCw className={`h-4 w-4 ${restartMutation.isPending ? 'animate-spin' : ''}`} />
        </button>
        {isConnected && (
          <button
            onClick={() => setConfirmLogout(true)}
            disabled={logoutMutation.isPending}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 disabled:opacity-50 transition-colors"
            title="Cerrar sesion de WhatsApp"
          >
            <LogOut className="h-4 w-4" />
          </button>
        )}
        <button
          onClick={() => onEdit(line)}
          className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
          title="Editar"
        >
          <Pencil className="h-4 w-4" />
        </button>
        <button
          onClick={() => onDelete(line)}
          className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 transition-colors"
          title="Eliminar"
        >
          <Trash2 className="h-4 w-4" />
        </button>
      </div>

      {/* Confirm Restart */}
      {confirmRestart && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={() => setConfirmRestart(false)}>
          <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-sm font-semibold text-gray-900 mb-2">Reiniciar instancia</h3>
            <p className="text-sm text-gray-600 mb-4">
              Esto reiniciara la instancia "{line.displayName}". La conexion se interrumpira brevemente.
            </p>
            <div className="flex justify-end gap-2">
              <button onClick={() => setConfirmRestart(false)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">Cancelar</button>
              <button
                onClick={() => { restartMutation.mutate(line.id); setConfirmRestart(false) }}
                className="rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 transition-colors"
              >
                Reiniciar
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Confirm Logout */}
      {confirmLogout && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={() => setConfirmLogout(false)}>
          <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-sm font-semibold text-gray-900 mb-2">Cerrar sesion de WhatsApp</h3>
            <p className="text-sm text-gray-600 mb-4">
              Se cerrara la sesion de "{line.displayName}" y necesitaras escanear un nuevo QR.
            </p>
            <div className="flex justify-end gap-2">
              <button onClick={() => setConfirmLogout(false)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">Cancelar</button>
              <button
                onClick={() => { logoutMutation.mutate(line.id); setConfirmLogout(false) }}
                className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700 transition-colors"
              >
                Cerrar sesion
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

// ── QR Modal ──────────────────────────────────────
function QrModal({ lineId, onClose }: { lineId: string; onClose: () => void }) {
  const { data: qrUrl, isLoading, isError } = useAdminLineQr(lineId)
  const { data: statusData } = useAdminLineStatus(lineId)

  // Auto-close when connected
  if (statusData?.status === 'authenticated' && qrUrl) {
    setTimeout(onClose, 1500)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold text-gray-900">Vincular WhatsApp</h3>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex flex-col items-center py-4">
          {isLoading ? (
            <div className="flex h-64 w-64 items-center justify-center">
              <Loader2 className="h-8 w-8 animate-spin text-gray-400" />
            </div>
          ) : isError ? (
            <div className="flex h-64 w-64 flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300">
              <QrCode className="h-16 w-16 text-gray-300" />
              <p className="mt-2 text-xs text-gray-400">No se pudo obtener el QR</p>
              <p className="mt-1 text-xs text-gray-400">Intenta reiniciar la instancia primero</p>
            </div>
          ) : qrUrl ? (
            <img src={qrUrl} alt="QR Code" className="h-64 w-64 rounded-lg border border-gray-200 p-2" />
          ) : (
            <div className="flex h-64 w-64 flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300">
              <QrCode className="h-16 w-16 text-gray-300" />
              <p className="mt-2 text-xs text-gray-400 text-center">
                {statusData?.status === 'authenticated'
                  ? 'La instancia ya esta conectada'
                  : 'QR no disponible. Reinicia la instancia e intenta de nuevo.'}
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
              <p className="mt-1 text-xs text-gray-500">
                Menu &rarr; Dispositivos vinculados &rarr; Vincular dispositivo
              </p>
              <p className="mt-2 text-xs text-gray-400">
                El QR se actualiza automaticamente cada 15 segundos
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Main Page ─────────────────────────────────────
export function AdminWhatsAppPage() {
  const { data: lines, isLoading } = useAdminWhatsAppLines()
  const createMut = useAdminCreateWhatsAppLine()
  const updateMut = useAdminUpdateWhatsAppLine()
  const deleteMut = useAdminDeleteWhatsAppLine()

  const [showForm, setShowForm] = useState(false)
  const [editLine, setEditLine] = useState<AdminWhatsAppLine | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<AdminWhatsAppLine | null>(null)
  const [qrLineId, setQrLineId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  // Create form
  const createForm = useForm<CreateForm>({
    resolver: zodResolver(createSchema),
    defaultValues: { displayName: '', phoneNumber: '', instanceId: '', apiToken: '' },
  })

  // Edit form
  const editForm = useForm<EditForm>({
    resolver: zodResolver(editSchema),
  })

  const onCreateSubmit = async (data: CreateForm) => {
    setError(null)
    try {
      await createMut.mutateAsync(data)
      createForm.reset()
      setShowForm(false)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setError(axiosErr.response?.data?.error ?? 'Error al crear.')
    }
  }

  const openEdit = (line: AdminWhatsAppLine) => {
    setEditLine(line)
    setShowForm(false)
    editForm.reset({
      displayName: line.displayName,
      phoneNumber: line.phoneNumber,
      instanceId: line.instanceId,
      apiToken: '',
      isActive: line.isActive,
    })
  }

  const onEditSubmit = async (data: EditForm) => {
    if (!editLine) return
    try {
      await updateMut.mutateAsync({
        id: editLine.id,
        displayName: data.displayName,
        phoneNumber: data.phoneNumber ?? '',
        instanceId: data.instanceId,
        apiToken: data.apiToken || undefined,
        isActive: data.isActive,
      })
      setEditLine(null)
    } catch {
      editForm.setError('displayName', { message: 'Error al actualizar' })
    }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    await deleteMut.mutateAsync(deleteTarget.id)
    setDeleteTarget(null)
  }

  return (
    <div>
      {/* Header */}
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Lineas de WhatsApp</h1>
          <p className="text-sm text-gray-500">Administra las lineas de WhatsApp para pruebas de agentes</p>
        </div>
        <button
          onClick={() => { setShowForm(!showForm); setEditLine(null) }}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Agregar linea
        </button>
      </div>

      {/* Error message */}
      {error && (
        <div className="mb-4 flex items-center justify-between rounded-md bg-red-50 p-3 text-sm text-red-700">
          {error}
          <button onClick={() => setError(null)} className="text-red-500 hover:text-red-700"><X className="h-4 w-4" /></button>
        </div>
      )}

      {/* Create form */}
      {showForm && (
        <div className="mb-6 rounded-lg border border-green-200 bg-green-50 p-5 shadow-sm">
          <h3 className="mb-4 text-sm font-semibold text-gray-900">Nueva linea de WhatsApp</h3>
          <form onSubmit={createForm.handleSubmit(onCreateSubmit)} className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Nombre *</label>
                <input {...createForm.register('displayName')} placeholder="Ej: Linea de prueba" className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-green-500 focus:outline-none focus:ring-1 focus:ring-green-500" />
                {createForm.formState.errors.displayName && <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.displayName.message}</p>}
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Telefono</label>
                <input {...createForm.register('phoneNumber')} placeholder="+507 6xxx-xxxx" className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-green-500 focus:outline-none focus:ring-1 focus:ring-green-500" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Instance ID (solo numero) *</label>
                <input {...createForm.register('instanceId')} placeholder="Ej: 140984" className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-green-500 focus:outline-none focus:ring-1 focus:ring-green-500" />
                {createForm.formState.errors.instanceId && <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.instanceId.message}</p>}
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">API Token *</label>
                <input {...createForm.register('apiToken')} type="password" placeholder="Token de UltraMsg" className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-green-500 focus:outline-none focus:ring-1 focus:ring-green-500" />
                {createForm.formState.errors.apiToken && <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.apiToken.message}</p>}
              </div>
            </div>
            <div className="flex justify-end gap-3">
              <button type="button" onClick={() => setShowForm(false)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">Cancelar</button>
              <button type="submit" disabled={createMut.isPending} className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors">
                {createMut.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
                Crear linea
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Edit form */}
      {editLine && (
        <div className="mb-6 rounded-lg border border-amber-200 bg-amber-50 p-5 shadow-sm">
          <h3 className="mb-4 text-sm font-semibold text-gray-900">Editar: {editLine.displayName}</h3>
          <form onSubmit={editForm.handleSubmit(onEditSubmit)} className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Nombre *</label>
                <input {...editForm.register('displayName')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500" />
                {editForm.formState.errors.displayName && <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.displayName.message}</p>}
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Telefono</label>
                <input {...editForm.register('phoneNumber')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Instance ID</label>
                <input {...editForm.register('instanceId')} className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500" />
                {editForm.formState.errors.instanceId && <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.instanceId.message}</p>}
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Token (dejar vacio para no cambiar)</label>
                <input {...editForm.register('apiToken')} type="password" placeholder="Dejar vacio para no cambiar" className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500" />
              </div>
            </div>
            <div className="flex items-center gap-3">
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input type="checkbox" {...editForm.register('isActive')} className="rounded border-gray-300" />
                Activa
              </label>
            </div>
            <div className="flex justify-end gap-3">
              <button type="button" onClick={() => setEditLine(null)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">Cancelar</button>
              <button type="submit" disabled={updateMut.isPending} className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors">
                {updateMut.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
                Actualizar
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Lines list */}
      {isLoading ? (
        <div className="py-12 text-center text-gray-400">
          <Loader2 className="mx-auto h-8 w-8 animate-spin text-gray-300" />
          <p className="mt-2 text-sm">Cargando lineas...</p>
        </div>
      ) : !lines?.length ? (
        <div className="py-16 text-center">
          <Phone className="mx-auto h-12 w-12 text-gray-300" />
          <h3 className="mt-2 text-sm font-semibold text-gray-900">Sin lineas de WhatsApp</h3>
          <p className="mt-1 text-sm text-gray-500">Crea una linea para probar tus agentes.</p>
        </div>
      ) : (
        <div className="space-y-2">
          {lines.map((line) => (
            <LineCard
              key={line.id}
              line={line}
              onEdit={openEdit}
              onDelete={setDeleteTarget}
              onShowQr={setQrLineId}
            />
          ))}
        </div>
      )}

      {/* QR Modal */}
      {qrLineId && <QrModal lineId={qrLineId} onClose={() => setQrLineId(null)} />}

      {/* Delete confirm */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={() => setDeleteTarget(null)}>
          <div className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-sm font-semibold text-gray-900 mb-2">Eliminar linea</h3>
            <p className="text-sm text-gray-600 mb-4">
              Se eliminara la linea "{deleteTarget.displayName}". Esta accion no se puede deshacer.
            </p>
            <div className="flex justify-end gap-2">
              <button onClick={() => setDeleteTarget(null)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors">Cancelar</button>
              <button
                onClick={handleDelete}
                disabled={deleteMut.isPending}
                className="flex items-center gap-2 rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50 transition-colors"
              >
                {deleteMut.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
                Eliminar
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
