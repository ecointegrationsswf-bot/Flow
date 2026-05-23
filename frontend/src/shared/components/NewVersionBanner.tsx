import { RefreshCw } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useVersionCheck } from '@/shared/hooks/useVersionCheck'

/**
 * Detecta automáticamente cuando se deployó una nueva versión del frontend
 * mientras el usuario tenía la pestaña abierta, y recarga la página sin
 * intervención manual.
 *
 * Estrategia:
 *  - Si la pestaña está en BACKGROUND (document.hidden) → reload inmediato.
 *    El usuario no se molesta porque no está viendo la app.
 *  - Si la pestaña está en FOREGROUND → muestra banner con countdown de 10s.
 *    El usuario tiene la oportunidad de hacer "Cancelar" si está en medio de
 *    typing/formulario importante. Pasado el countdown, reload automático.
 *
 * El polling de useVersionCheck (cada 30s + en visibilitychange) hace el resto.
 */
const COUNTDOWN_SECONDS = 10

export function NewVersionBanner() {
  const hasNewVersion = useVersionCheck()
  const [cancelled, setCancelled] = useState(false)
  const [secondsLeft, setSecondsLeft] = useState(COUNTDOWN_SECONDS)

  // Cuando aparece la nueva versión: si el tab está oculto, reload inmediato.
  // Si está visible, iniciar countdown para auto-reload.
  useEffect(() => {
    if (!hasNewVersion || cancelled) return

    if (document.visibilityState === 'hidden') {
      window.location.reload()
      return
    }

    setSecondsLeft(COUNTDOWN_SECONDS)
    const interval = window.setInterval(() => {
      setSecondsLeft(prev => {
        if (prev <= 1) {
          window.clearInterval(interval)
          window.location.reload()
          return 0
        }
        return prev - 1
      })
    }, 1000)

    return () => window.clearInterval(interval)
  }, [hasNewVersion, cancelled])

  // Si el usuario cambió a otro tab durante el countdown, hacemos reload inmediato.
  useEffect(() => {
    if (!hasNewVersion || cancelled) return
    const onHide = () => {
      if (document.visibilityState === 'hidden') window.location.reload()
    }
    document.addEventListener('visibilitychange', onHide)
    return () => document.removeEventListener('visibilitychange', onHide)
  }, [hasNewVersion, cancelled])

  if (!hasNewVersion || cancelled) return null

  return (
    <div className="fixed bottom-4 right-4 z-[100] flex max-w-sm items-start gap-3 rounded-lg border border-blue-200 bg-white px-4 py-3 shadow-lg ring-1 ring-blue-500/10">
      <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-blue-100">
        <RefreshCw className="h-4 w-4 animate-spin text-blue-600" />
      </div>
      <div className="flex-1">
        <p className="text-sm font-semibold text-gray-900">
          Nueva versión detectada
        </p>
        <p className="mt-0.5 text-xs text-gray-600">
          Actualizando automáticamente en <b>{secondsLeft}s</b>...
        </p>
        <div className="mt-2 flex items-center gap-2">
          <button
            onClick={() => window.location.reload()}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
          >
            Recargar ahora
          </button>
          <button
            onClick={() => setCancelled(true)}
            className="text-xs font-medium text-gray-500 hover:text-gray-700"
            title="No recargar (la próxima carga natural traerá la versión nueva igualmente)"
          >
            Cancelar
          </button>
        </div>
      </div>
    </div>
  )
}
