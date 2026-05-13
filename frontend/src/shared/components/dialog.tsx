import { useEffect, useState } from 'react'
import { AlertTriangle, CheckCircle, Info, X, XCircle } from 'lucide-react'

// ── API imperativa ──────────────────────────────────────────────────────────
// Reemplazo de window.confirm / window.alert por modales React.
// Uso:
//   if (await confirmDialog({ title: 'Eliminar?', description: '...' })) { ... }
//   toast.error('Mensaje de error')
//   toast.success('Listo')

export interface ConfirmOptions {
  title: string
  description?: string
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'danger' | 'default'
}

type ConfirmHandler = (opts: ConfirmOptions) => Promise<boolean>
type ToastKind = 'info' | 'success' | 'error' | 'warning'
type ToastHandler = (msg: string, kind?: ToastKind) => void

let confirmHandler: ConfirmHandler | null = null
let toastHandler: ToastHandler | null = null

export function confirmDialog(opts: ConfirmOptions): Promise<boolean> {
  if (!confirmHandler) {
    console.warn('DialogHost no está montado — fallback a window.confirm')
    return Promise.resolve(window.confirm(`${opts.title}\n\n${opts.description ?? ''}`))
  }
  return confirmHandler(opts)
}

export const toast = {
  info:    (msg: string) => toastHandler?.(msg, 'info'),
  success: (msg: string) => toastHandler?.(msg, 'success'),
  error:   (msg: string) => toastHandler?.(msg, 'error'),
  warning: (msg: string) => toastHandler?.(msg, 'warning'),
}

// ── Componente host ─────────────────────────────────────────────────────────
interface ConfirmState extends ConfirmOptions {
  resolve: (v: boolean) => void
}

interface ToastItem {
  id: number
  msg: string
  kind: ToastKind
}

export function DialogHost() {
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null)
  const [toasts, setToasts] = useState<ToastItem[]>([])

  useEffect(() => {
    confirmHandler = (opts) =>
      new Promise<boolean>((resolve) => setConfirmState({ ...opts, resolve }))

    toastHandler = (msg, kind = 'info') => {
      const id = Date.now() + Math.random()
      setToasts((prev) => [...prev, { id, msg, kind }])
      setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 4500)
    }

    return () => {
      confirmHandler = null
      toastHandler = null
    }
  }, [])

  const closeConfirm = (result: boolean) => {
    confirmState?.resolve(result)
    setConfirmState(null)
  }

  return (
    <>
      {/* Confirm modal */}
      {confirmState && (
        <div
          className="fixed inset-0 z-[1000] flex items-center justify-center bg-black/50 p-4"
          onClick={() => closeConfirm(false)}
        >
          <div
            className="w-full max-w-md rounded-xl bg-white shadow-xl"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="p-6">
              <div className="flex items-start gap-3">
                {confirmState.variant === 'danger' && (
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-red-100">
                    <AlertTriangle className="h-5 w-5 text-red-600" />
                  </div>
                )}
                <div className="flex-1">
                  <h3 className="text-base font-semibold text-gray-900">{confirmState.title}</h3>
                  {confirmState.description && (
                    <p className="mt-1.5 text-sm text-gray-600">{confirmState.description}</p>
                  )}
                </div>
              </div>
              <div className="mt-6 flex justify-end gap-2">
                <button
                  onClick={() => closeConfirm(false)}
                  className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
                >
                  {confirmState.cancelLabel ?? 'Cancelar'}
                </button>
                <button
                  onClick={() => closeConfirm(true)}
                  className={`rounded-lg px-4 py-2 text-sm font-medium text-white transition-colors ${
                    confirmState.variant === 'danger'
                      ? 'bg-red-600 hover:bg-red-700'
                      : 'bg-blue-600 hover:bg-blue-700'
                  }`}
                >
                  {confirmState.confirmLabel ?? 'Confirmar'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Toast stack */}
      {toasts.length > 0 && (
        <div className="fixed top-4 right-4 z-[1100] flex flex-col gap-2">
          {toasts.map((t) => (
            <ToastCard key={t.id} item={t} onClose={() => setToasts((p) => p.filter((x) => x.id !== t.id))} />
          ))}
        </div>
      )}
    </>
  )
}

function ToastCard({ item, onClose }: { item: ToastItem; onClose: () => void }) {
  const styles: Record<ToastKind, { bg: string; icon: JSX.Element }> = {
    info:    { bg: 'border-blue-200 bg-blue-50',     icon: <Info       className="h-5 w-5 text-blue-600" /> },
    success: { bg: 'border-green-200 bg-green-50',   icon: <CheckCircle className="h-5 w-5 text-green-600" /> },
    error:   { bg: 'border-red-200 bg-red-50',       icon: <XCircle    className="h-5 w-5 text-red-600" /> },
    warning: { bg: 'border-amber-200 bg-amber-50',   icon: <AlertTriangle className="h-5 w-5 text-amber-600" /> },
  }
  const s = styles[item.kind]
  return (
    <div className={`flex w-80 items-start gap-3 rounded-lg border ${s.bg} p-3 shadow-lg`}>
      {s.icon}
      <p className="flex-1 text-sm text-gray-800 whitespace-pre-wrap">{item.msg}</p>
      <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
        <X className="h-4 w-4" />
      </button>
    </div>
  )
}
