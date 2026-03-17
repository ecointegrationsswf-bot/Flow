import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import {
  Plus, Trash2, Pencil, RefreshCw, LogOut, QrCode, Phone, X, Loader2,
} from 'lucide-react'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { EmptyState } from '@/shared/components/EmptyState'
import {
  useWhatsAppLines, useCreateWhatsAppLine, useUpdateWhatsAppLine, useDeleteWhatsAppLine,
  useLineStatus, useLineQr, useRestartLine, useLogoutLine,
} from '@/shared/hooks/useWhatsAppLines'
import type { WhatsAppLine } from '@/shared/types'

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
  line: WhatsAppLine
  onEdit: (line: WhatsAppLine) => void
  onDelete: (line: WhatsAppLine) => void
  onShowQr: (lineId: string) => void
}) {
  const { data: statusData, isError } = useLineStatus(line.id)
  const restartMutation = useRestartLine()
  const logoutMutation = useLogoutLine()

  const currentStatus = statusData?.status ?? 'loading'
  const sem = getSemaphore(isError ? 'disconnected' : currentStatus)
  const isConnected = currentStatus === 'authenticated'
  const needsQr = ['qr', 'disconnected', 'initialize', 'standby'].includes(currentStatus) || isError

  const [showRestart, setShowRestart] = useState(false)
  const [showLogout, setShowLogout] = useState(false)

  return (
    <div className="flex items-center justify-between rounded-lg border border-gray-200 bg-white px-4 py-3 shadow-sm">
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
        {needsQr && (
          <button
            onClick={() => onShowQr(line.id)}
            className="flex items-center gap-1 rounded-md bg-green-600 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-green-700"
            title="Vincular por QR"
          >
            <QrCode className="h-3.5 w-3.5" />
            Vincular
          </button>
        )}
        <button
          onClick={() => setShowRestart(true)}
          disabled={restartMutation.isPending}
          className="rounded p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700 disabled:opacity-50"
          title="Reiniciar"
        >
          <RefreshCw className={`h-4 w-4 ${restartMutation.isPending ? 'animate-spin' : ''}`} />
        </button>
        {isConnected && (
          <button
            onClick={() => setShowLogout(true)}
            disabled={logoutMutation.isPending}
            className="rounded p-1.5 text-gray-400 hover:bg-red-50 hover:text-red-600 disabled:opacity-50"
            title="Cerrar sesion"
          >
            <LogOut className="h-4 w-4" />
          </button>
        )}
        <button
          onClick={() => onEdit(line)}
          className="rounded p-1.5 text-gray-400 hover:bg-blue-50 hover:text-blue-600"
          title="Editar"
        >
          <Pencil className="h-4 w-4" />
        </button>
        <button
          onClick={() => onDelete(line)}
          className="rounded p-1.5 text-gray-400 hover:bg-red-50 hover:text-red-600"
          title="Eliminar"
        >
          <Trash2 className="h-4 w-4" />
        </button>

        {/* Restart confirm */}
        <ConfirmDialog
          open={showRestart}
          onClose={() => setShowRestart(false)}
          onConfirm={() => { restartMutation.mutate(line.id); setShowRestart(false) }}
          title="Reiniciar instancia"
          description={`Esto reiniciara la instancia "${line.displayName}". La conexion se interrumpira brevemente.`}
          confirmLabel="Reiniciar"
        />
        {/* Logout confirm */}
        <ConfirmDialog
          open={showLogout}
          onClose={() => setShowLogout(false)}
          onConfirm={() => { logoutMutation.mutate(line.id); setShowLogout(false) }}
          title="Cerrar sesion de WhatsApp"
          description={`Se cerrara la sesion de "${line.displayName}" y necesitaras escanear un nuevo QR.`}
          confirmLabel="Cerrar sesion"
          variant="danger"
        />
      </div>
    </div>
  )
}

// ── QR Modal ──────────────────────────────────────
function QrModal({ lineId, onClose }: { lineId: string; onClose: () => void }) {
  const { data: qrUrl, isLoading, isError } = useLineQr(lineId)
  const { data: statusData } = useLineStatus(lineId)

  // Auto-close when connected
  if (statusData?.status === 'authenticated') {
    setTimeout(onClose, 500)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-sm font-semibold text-gray-900">Vincular WhatsApp</h3>
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
            </div>
          ) : qrUrl ? (
            <img src={qrUrl} alt="QR Code" className="h-64 w-64 rounded-lg border border-gray-200 p-2" />
          ) : (
            <div className="flex h-64 w-64 flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300">
              <QrCode className="h-16 w-16 text-gray-300" />
              <p className="mt-2 text-xs text-gray-400">La instancia ya esta conectada, no necesita QR</p>
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
                Menu → Dispositivos vinculados → Vincular dispositivo
              </p>
              <p className="mt-2 text-xs text-gray-400">
                El QR se actualiza automaticamente
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Main Tab ──────────────────────────────────────
export function WhatsAppTab() {
  const { data: lines, isLoading } = useWhatsAppLines()
  const createMutation = useCreateWhatsAppLine()
  const updateMutation = useUpdateWhatsAppLine()
  const deleteMutation = useDeleteWhatsAppLine()

  const [showForm, setShowForm] = useState(false)
  const [editLine, setEditLine] = useState<WhatsAppLine | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<WhatsAppLine | null>(null)
  const [qrLineId, setQrLineId] = useState<string | null>(null)

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
    try {
      await createMutation.mutateAsync(data)
      createForm.reset()
      setShowForm(false)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      createForm.setError('instanceId', { message: axiosErr.response?.data?.error ?? 'Error al crear' })
    }
  }

  const openEdit = (line: WhatsAppLine) => {
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
      await updateMutation.mutateAsync({
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
    await deleteMutation.mutateAsync(deleteTarget.id)
    setDeleteTarget(null)
  }

  if (isLoading) return <LoadingSpinner />

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-sm font-semibold text-gray-900">
            Lineas de WhatsApp {lines ? `(${lines.length})` : ''}
          </h3>
          <p className="text-xs text-gray-500">
            Administra los numeros de WhatsApp vinculados a tu cuenta
          </p>
        </div>
        <button
          onClick={() => { setShowForm(!showForm); setEditLine(null) }}
          className="flex items-center gap-1.5 rounded-md bg-green-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-700"
        >
          <Plus className="h-3.5 w-3.5" /> Agregar linea
        </button>
      </div>

      {/* Create form */}
      {showForm && (
        <form
          onSubmit={createForm.handleSubmit(onCreateSubmit)}
          className="rounded-lg border border-green-200 bg-green-50 p-4"
        >
          <h4 className="mb-3 text-sm font-medium text-gray-900">Nueva linea de WhatsApp</h4>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <input
                placeholder="Nombre (ej: Cobros Principal)"
                {...createForm.register('displayName')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {createForm.formState.errors.displayName && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.displayName.message}</p>
              )}
            </div>
            <div>
              <input
                placeholder="Telefono (ej: +50760001234)"
                {...createForm.register('phoneNumber')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
            </div>
            <div>
              <input
                placeholder="ID de Instancia (solo el numero, ej: 140984)"
                {...createForm.register('instanceId')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {createForm.formState.errors.instanceId && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.instanceId.message}</p>
              )}
            </div>
            <div>
              <input
                placeholder="Token de UltraMsg"
                type="password"
                {...createForm.register('apiToken')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {createForm.formState.errors.apiToken && (
                <p className="mt-1 text-xs text-red-600">{createForm.formState.errors.apiToken.message}</p>
              )}
            </div>
          </div>
          <div className="mt-3 flex items-center gap-2">
            <button
              type="submit"
              disabled={createMutation.isPending}
              className="flex items-center gap-1.5 rounded-md bg-green-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-700 disabled:opacity-50"
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
      {editLine && (
        <form
          onSubmit={editForm.handleSubmit(onEditSubmit)}
          className="rounded-lg border border-amber-200 bg-amber-50 p-4"
        >
          <h4 className="mb-3 text-sm font-medium text-gray-900">Editar: {editLine.displayName}</h4>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <input
                placeholder="Nombre"
                {...editForm.register('displayName')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {editForm.formState.errors.displayName && (
                <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.displayName.message}</p>
              )}
            </div>
            <input
              placeholder="Telefono"
              {...editForm.register('phoneNumber')}
              className="rounded-md border border-gray-300 px-3 py-2 text-sm"
            />
            <div>
              <label className="block text-xs font-medium text-gray-500 mb-1">Instance ID</label>
              <input
                {...editForm.register('instanceId')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              {editForm.formState.errors.instanceId && (
                <p className="mt-1 text-xs text-red-600">{editForm.formState.errors.instanceId.message}</p>
              )}
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500 mb-1">Token</label>
              <input
                type="password"
                placeholder="Dejar vacio para no cambiar"
                {...editForm.register('apiToken')}
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
            </div>
          </div>
          <div className="mt-3 flex items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" {...editForm.register('isActive')} className="rounded border-gray-300" />
              Activa
            </label>
          </div>
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
              onClick={() => setEditLine(null)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancelar
            </button>
          </div>
        </form>
      )}

      {/* Lines list */}
      {lines && lines.length > 0 ? (
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
      ) : (
        <EmptyState
          icon={Phone}
          title="Sin lineas de WhatsApp"
          description="Agrega una linea para vincular un numero de WhatsApp con UltraMsg"
        />
      )}

      {/* QR Modal */}
      {qrLineId && <QrModal lineId={qrLineId} onClose={() => setQrLineId(null)} />}

      {/* Delete confirm */}
      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title="Eliminar linea"
        description={`Se eliminara la linea "${deleteTarget?.displayName}". Los agentes que usen este numero dejaran de funcionar.`}
        confirmLabel="Eliminar"
        variant="danger"
      />
    </div>
  )
}
