import { useEffect, useState, useCallback } from 'react'
import { CheckCircle2, XCircle, AlertCircle, Info, X } from 'lucide-react'

export type ToastType = 'success' | 'error' | 'warning' | 'info'

export interface ToastMessage {
  id: string
  type: ToastType
  message: string
  duration?: number
}

// ─── Icono y colores por tipo ─────────────────────────────────────────────────

const TOAST_STYLES: Record<ToastType, { icon: JSX.Element; classes: string }> = {
  success: {
    icon: <CheckCircle2 className="h-5 w-5 text-green-500 shrink-0" />,
    classes: 'bg-white border border-green-200 text-green-800',
  },
  error: {
    icon: <XCircle className="h-5 w-5 text-red-500 shrink-0" />,
    classes: 'bg-white border border-red-200 text-red-800',
  },
  warning: {
    icon: <AlertCircle className="h-5 w-5 text-amber-500 shrink-0" />,
    classes: 'bg-white border border-amber-200 text-amber-800',
  },
  info: {
    icon: <Info className="h-5 w-5 text-blue-500 shrink-0" />,
    classes: 'bg-white border border-blue-200 text-blue-800',
  },
}

// ─── Toast individual ─────────────────────────────────────────────────────────

function ToastItem({ toast, onRemove }: { toast: ToastMessage; onRemove: (id: string) => void }) {
  const [visible, setVisible] = useState(false)
  const duration = toast.duration ?? 4000
  const style = TOAST_STYLES[toast.type]

  useEffect(() => {
    // Entrada con pequeño delay para animar
    const show = setTimeout(() => setVisible(true), 10)
    const hide = setTimeout(() => { setVisible(false); setTimeout(() => onRemove(toast.id), 300) }, duration)
    return () => { clearTimeout(show); clearTimeout(hide) }
  }, [toast.id, duration, onRemove])

  return (
    <div
      className={`flex items-start gap-3 rounded-lg px-4 py-3 shadow-lg transition-all duration-300 ${style.classes} ${
        visible ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-2'
      }`}
    >
      {style.icon}
      <p className="flex-1 text-sm font-medium leading-snug">{toast.message}</p>
      <button
        onClick={() => { setVisible(false); setTimeout(() => onRemove(toast.id), 300) }}
        className="ml-1 rounded p-0.5 opacity-60 hover:opacity-100"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  )
}

// ─── Contenedor de toasts (portal-like, fixed) ────────────────────────────────

export function ToastContainer({ toasts, onRemove }: {
  toasts: ToastMessage[]
  onRemove: (id: string) => void
}) {
  if (toasts.length === 0) return null
  return (
    <div className="fixed bottom-5 right-5 z-50 flex flex-col gap-2 w-80 max-w-[90vw]">
      {toasts.map((t) => (
        <ToastItem key={t.id} toast={t} onRemove={onRemove} />
      ))}
    </div>
  )
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

let _counter = 0

export function useToast() {
  const [toasts, setToasts] = useState<ToastMessage[]>([])

  const remove = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  const show = useCallback((type: ToastType, message: string, duration?: number) => {
    const id = `toast-${++_counter}`
    setToasts((prev) => [...prev, { id, type, message, duration }])
  }, [])

  const toast = {
    success: (msg: string, duration?: number) => show('success', msg, duration),
    error:   (msg: string, duration?: number) => show('error',   msg, duration),
    warning: (msg: string, duration?: number) => show('warning', msg, duration),
    info:    (msg: string, duration?: number) => show('info',    msg, duration),
  }

  return { toasts, remove, toast }
}
