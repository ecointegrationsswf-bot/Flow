import { CheckCircle2, XCircle, AlertCircle, Info, X } from 'lucide-react'

export type MessageDialogKind = 'success' | 'error' | 'warning' | 'info'

interface Props {
  open: boolean
  onClose: () => void
  kind: MessageDialogKind
  title: string
  description?: string
  /** Detalle técnico colapsable (stack, JSON, etc.). */
  detail?: string
  primaryLabel?: string
  onPrimary?: () => void
  secondaryLabel?: string
}

/**
 * Modal centrado, con icono + título grande + descripción. Usado para resultados
 * de operaciones destacadas (guardar maestro, error inesperado, etc.) cuando un
 * toast en la esquina pasa desapercibido.
 */
export function MessageDialog({
  open, onClose, kind, title, description, detail,
  primaryLabel, onPrimary, secondaryLabel = 'Cerrar',
}: Props) {
  if (!open) return null

  const palette: Record<MessageDialogKind, { ring: string; ic: JSX.Element; btn: string }> = {
    success: {
      ring: 'bg-green-50 text-green-600 ring-green-200',
      ic: <CheckCircle2 className="h-7 w-7" />,
      btn: 'bg-green-600 hover:bg-green-700',
    },
    error: {
      ring: 'bg-red-50 text-red-600 ring-red-200',
      ic: <XCircle className="h-7 w-7" />,
      btn: 'bg-red-600 hover:bg-red-700',
    },
    warning: {
      ring: 'bg-amber-50 text-amber-600 ring-amber-200',
      ic: <AlertCircle className="h-7 w-7" />,
      btn: 'bg-amber-600 hover:bg-amber-700',
    },
    info: {
      ring: 'bg-blue-50 text-blue-600 ring-blue-200',
      ic: <Info className="h-7 w-7" />,
      btn: 'bg-blue-600 hover:bg-blue-700',
    },
  }
  const p = palette[kind]

  return (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
      onKeyDown={(e) => { if (e.key === 'Escape') onClose() }}
    >
      <div
        className="w-full max-w-md overflow-hidden rounded-xl bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-6 pt-6 pb-4">
          <div className="flex items-start gap-4">
            <span className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-full ring-4 ${p.ring}`}>{p.ic}</span>
            <div className="flex-1 min-w-0">
              <h3 className="text-base font-semibold text-gray-900 leading-snug">{title}</h3>
              {description && (
                <p className="mt-1 text-sm text-gray-600 leading-relaxed whitespace-pre-line">{description}</p>
              )}
              {detail && (
                <details className="mt-3 rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-xs text-gray-600">
                  <summary className="cursor-pointer font-medium text-gray-700">Ver detalle técnico</summary>
                  <pre className="mt-2 whitespace-pre-wrap break-words font-mono text-[11px] text-gray-700">{detail}</pre>
                </details>
              )}
            </div>
            <button
              onClick={onClose}
              className="ml-2 rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
              aria-label="Cerrar"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
        <div className="flex justify-end gap-2 border-t border-gray-100 bg-gray-50 px-6 py-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            {secondaryLabel}
          </button>
          {primaryLabel && onPrimary && (
            <button
              type="button"
              onClick={() => { onPrimary(); onClose() }}
              className={`rounded-md px-3 py-1.5 text-sm font-medium text-white ${p.btn}`}
            >
              {primaryLabel}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
