import { useEffect, useState } from 'react'
import { AlertTriangle, CheckCircle, Info, X, XCircle } from 'lucide-react'
import { MessageDialog, type MessageDialogKind } from './MessageDialog'

// ── API imperativa ──────────────────────────────────────────────────────────
// Reemplazo de window.confirm / window.alert / window.prompt por modales React.
// 🚫 PROHIBIDO usar alert/confirm/prompt nativos (regla ESLint no-alert). Usar:
//   if (await confirmDialog({ title: 'Eliminar?', description: '...' })) { ... }
//   await alertDialog({ kind: 'error', title: 'Validación fallida', description: '...' })
//   const url = await promptDialog({ title: 'URL del link', defaultValue: 'https://' })
//   toast.error('Mensaje de error')  /  toast.success('Listo')

export interface ConfirmOptions {
  title: string
  description?: string
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'danger' | 'default'
}

export interface AlertOptions {
  /** Tipo visual del modal — define icono + color. Default: 'error'. */
  kind?: MessageDialogKind
  title: string
  description?: string
  /** Detalle técnico colapsable (JSON crudo, stack trace, etc.). */
  detail?: string
  closeLabel?: string
}

export interface PromptOptions {
  title: string
  description?: string
  placeholder?: string
  defaultValue?: string
  confirmLabel?: string
  cancelLabel?: string
  inputType?: 'text' | 'url' | 'email' | 'number'
}

type ConfirmHandler = (opts: ConfirmOptions) => Promise<boolean>
type AlertHandler = (opts: AlertOptions) => Promise<void>
type PromptHandler = (opts: PromptOptions) => Promise<string | null>
type ToastKind = 'info' | 'success' | 'error' | 'warning'
type ToastHandler = (msg: string, kind?: ToastKind) => void

let confirmHandler: ConfirmHandler | null = null
let alertHandler: AlertHandler | null = null
let promptHandler: PromptHandler | null = null
let toastHandler: ToastHandler | null = null

export function confirmDialog(opts: ConfirmOptions): Promise<boolean> {
  if (!confirmHandler) {
    // Sin fallback nativo (window.confirm está prohibido). Si <DialogHost/> no
    // está montado, devolvemos false y lo logueamos — nunca un diálogo nativo.
    console.error('DialogHost no está montado — confirmDialog ignorado (devuelve false).')
    return Promise.resolve(false)
  }
  return confirmHandler(opts)
}

/**
 * Modal imperativa para mostrar mensajes (validaciones, errores, info).
 * Usada por los interceptores de axios para surfacing automático de errores
 * de validación del backend (HTTP 400/409/422). También se puede llamar
 * manualmente desde cualquier handler para mostrar un mensaje sin toast.
 */
export function alertDialog(opts: AlertOptions): Promise<void> {
  if (!alertHandler) {
    console.error('DialogHost no está montado — alertDialog ignorado.')
    return Promise.resolve()
  }
  return alertHandler(opts)
}

/**
 * Modal imperativa de entrada de texto (reemplazo de window.prompt).
 * Resuelve con el string ingresado, o `null` si el usuario cancela
 * (misma semántica que window.prompt).
 */
export function promptDialog(opts: PromptOptions): Promise<string | null> {
  if (!promptHandler) {
    console.error('DialogHost no está montado — promptDialog ignorado (devuelve null).')
    return Promise.resolve(null)
  }
  return promptHandler(opts)
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

interface AlertState extends AlertOptions {
  resolve: () => void
}

interface PromptState extends PromptOptions {
  resolve: (v: string | null) => void
}

interface ToastItem {
  id: number
  msg: string
  kind: ToastKind
}

export function DialogHost() {
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null)
  const [alertState, setAlertState] = useState<AlertState | null>(null)
  const [promptState, setPromptState] = useState<PromptState | null>(null)
  const [toasts, setToasts] = useState<ToastItem[]>([])

  useEffect(() => {
    confirmHandler = (opts) =>
      new Promise<boolean>((resolve) => setConfirmState({ ...opts, resolve }))

    alertHandler = (opts) =>
      new Promise<void>((resolve) => setAlertState({ ...opts, resolve }))

    promptHandler = (opts) =>
      new Promise<string | null>((resolve) => setPromptState({ ...opts, resolve }))

    toastHandler = (msg, kind = 'info') => {
      const id = Date.now() + Math.random()
      setToasts((prev) => [...prev, { id, msg, kind }])
      setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 4500)
    }

    return () => {
      confirmHandler = null
      alertHandler = null
      promptHandler = null
      toastHandler = null
    }
  }, [])

  const closeConfirm = (result: boolean) => {
    confirmState?.resolve(result)
    setConfirmState(null)
  }

  const closeAlert = () => {
    alertState?.resolve()
    setAlertState(null)
  }

  const closePrompt = (value: string | null) => {
    promptState?.resolve(value)
    setPromptState(null)
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
                    <p className="mt-1.5 text-sm text-gray-600 whitespace-pre-wrap">{confirmState.description}</p>
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

      {/* Prompt modal (input) */}
      {promptState && (
        <PromptModal
          state={promptState}
          onCancel={() => closePrompt(null)}
          onConfirm={(v) => closePrompt(v)}
        />
      )}

      {/* Alert / validation modal */}
      <MessageDialog
        open={!!alertState}
        onClose={closeAlert}
        kind={alertState?.kind ?? 'error'}
        title={alertState?.title ?? ''}
        description={alertState?.description}
        detail={alertState?.detail}
        secondaryLabel={alertState?.closeLabel ?? 'Entendido'}
      />

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

function PromptModal({
  state,
  onCancel,
  onConfirm,
}: {
  state: PromptState
  onCancel: () => void
  onConfirm: (value: string) => void
}) {
  const [value, setValue] = useState(state.defaultValue ?? '')

  return (
    <div
      className="fixed inset-0 z-[1000] flex items-center justify-center bg-black/50 p-4"
      onClick={onCancel}
    >
      <div
        className="w-full max-w-md rounded-xl bg-white shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <form
          className="p-6"
          onSubmit={(e) => {
            e.preventDefault()
            onConfirm(value)
          }}
        >
          <h3 className="text-base font-semibold text-gray-900">{state.title}</h3>
          {state.description && (
            <p className="mt-1.5 text-sm text-gray-600 whitespace-pre-wrap">{state.description}</p>
          )}
          <input
            autoFocus
            type={state.inputType ?? 'text'}
            value={value}
            placeholder={state.placeholder}
            onChange={(e) => setValue(e.target.value)}
            className="mt-4 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
          <div className="mt-6 flex justify-end gap-2">
            <button
              type="button"
              onClick={onCancel}
              className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
            >
              {state.cancelLabel ?? 'Cancelar'}
            </button>
            <button
              type="submit"
              className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
            >
              {state.confirmLabel ?? 'Aceptar'}
            </button>
          </div>
        </form>
      </div>
    </div>
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
