import { RefreshCw, X } from 'lucide-react'
import { useState } from 'react'
import { useVersionCheck } from '@/shared/hooks/useVersionCheck'

/**
 * Banner flotante (bottom-right) que aparece cuando hay una nueva versión
 * del frontend deployada. El usuario puede recargar de inmediato o cerrar el
 * banner para seguir trabajando (la próxima navegación natural cargará la
 * versión nueva igual, porque index.html tiene Cache-Control: no-cache).
 *
 * Diseño:
 *  - No interrumpe el flujo de trabajo del usuario.
 *  - Aparece sutilmente en el rincón inferior derecho.
 *  - Botón principal "Recargar ahora" → window.location.reload().
 *  - X para descartar (volverá a aparecer si pasan otros 60s y la versión sigue siendo nueva).
 */
export function NewVersionBanner() {
  const hasNewVersion = useVersionCheck()
  const [dismissed, setDismissed] = useState(false)

  if (!hasNewVersion || dismissed) return null

  return (
    <div className="fixed bottom-4 right-4 z-[100] flex max-w-sm items-start gap-3 rounded-lg border border-blue-200 bg-white px-4 py-3 shadow-lg ring-1 ring-blue-500/10 animate-in slide-in-from-bottom-2">
      <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-blue-100">
        <RefreshCw className="h-4 w-4 text-blue-600" />
      </div>
      <div className="flex-1">
        <p className="text-sm font-semibold text-gray-900">Nueva versión disponible</p>
        <p className="mt-0.5 text-xs text-gray-600">
          Recargá para usar la versión más reciente. Tus cambios sin guardar se perderán.
        </p>
        <div className="mt-2 flex items-center gap-2">
          <button
            onClick={() => window.location.reload()}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
          >
            Recargar ahora
          </button>
          <button
            onClick={() => setDismissed(true)}
            className="text-xs font-medium text-gray-500 hover:text-gray-700"
          >
            Más tarde
          </button>
        </div>
      </div>
      <button
        onClick={() => setDismissed(true)}
        className="shrink-0 rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
        aria-label="Cerrar"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  )
}
