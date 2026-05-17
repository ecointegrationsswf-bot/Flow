import type { AxiosError, AxiosInstance } from 'axios'
import { alertDialog } from '@/shared/components/dialog'

/**
 * Interceptor de respuesta GLOBAL que captura errores de validación del
 * backend y los muestra automáticamente en una modal visible al usuario.
 *
 * Antes: cada formulario tenía que parsear `err.response.data.error` a mano
 * y mostrar el mensaje con un toast o modal local. Resultado: muchos
 * mensajes (ej. "El maestro debe tener un SystemPrompt") quedaban
 * únicamente en la consola del navegador y el usuario veía un botón que
 * "no hace nada".
 *
 * Ahora: cualquier request de escritura (POST/PUT/PATCH/DELETE) que
 * devuelva 400/409/422/500+ con un mensaje de error en el body, dispara
 * `alertDialog(...)` con el mensaje legible. Las queries (GET) no
 * disparan modal porque suelen ser polling de fondo.
 *
 * Opt-out: pasar `{ suppressGlobalErrorModal: true }` en el config del
 * request si el caller tiene su propio flujo de UI (ej. modal de "swap
 * de maestro primario" que requiere acción del usuario, no solo aviso).
 *
 * Bandera saliente: cuando el interceptor decide mostrar la modal,
 * marca `err.handledByGlobalDialog = true` para que los `onError` de
 * react-query puedan saltarse su propio fallback y no duplicar dialogs.
 */

// Códigos de error específicos que tienen UI dedicada en el frontend.
// El interceptor los deja pasar tal cual para que el handler local arme su flujo.
const SKIP_ERROR_CODES = new Set<string>([
  // CampaignTemplateFormPage muestra modal de "swap del maestro primario"
  'primary_template_swap_required',
])

interface ApiErrorBody {
  error?: string
  message?: string
  title?: string
  detail?: string
  errors?: Record<string, string | string[]>
  [k: string]: unknown
}

/** Convierte el body de error del API en un mensaje legible para el usuario. */
function extractApiMessage(data: unknown): string | null {
  if (!data) return null
  if (typeof data === 'string') return data
  if (typeof data !== 'object') return null

  const d = data as ApiErrorBody

  // Formato custom AgentFlow: { error: "...", field?: "..." }
  if (typeof d.error === 'string' && d.error.trim()) return d.error

  // ASP.NET ValidationProblemDetails: { title, errors: { field: [msgs] } }
  if (d.errors && typeof d.errors === 'object') {
    const lines: string[] = []
    for (const [field, msgs] of Object.entries(d.errors)) {
      const arr = Array.isArray(msgs) ? msgs : [String(msgs)]
      for (const m of arr) lines.push(field === '$' ? m : `${field}: ${m}`)
    }
    const title = typeof d.title === 'string' ? d.title : 'Datos inválidos'
    return [title, ...lines].filter(Boolean).join('\n')
  }

  if (typeof d.message === 'string' && d.message.trim()) return d.message
  if (typeof d.title === 'string' && d.title.trim()) return d.title
  if (typeof d.detail === 'string' && d.detail.trim()) return d.detail

  return null
}

function titleForStatus(status: number): string {
  if (status === 409) return 'Conflicto'
  if (status === 422) return 'Datos inválidos'
  if (status === 403) return 'Sin permisos'
  if (status === 404) return 'No encontrado'
  if (status >= 500) return 'Error del servidor'
  return 'Validación fallida'
}

/**
 * Engancha el interceptor en una instancia de axios. Llamar UNA vez por cliente
 * al construirlo (ver `adminClient.ts` y `client.ts`).
 *
 * @param onUnauthorized callback adicional que el caller usa para 401
 *   (logout y redirect). El interceptor se lo invoca antes de procesar
 *   la modal, así no se muestra "Sin permisos" justo antes de redirigir
 *   al login.
 */
export function attachGlobalErrorInterceptor(
  client: AxiosInstance,
  opts: { onUnauthorized?: (err: AxiosError) => void } = {},
) {
  client.interceptors.response.use(
    (res) => res,
    (err: AxiosError<ApiErrorBody>) => {
      const status = err.response?.status

      // 401 — siempre delegar al caller (suele hacer logout + redirect)
      if (status === 401) {
        opts.onUnauthorized?.(err)
        return Promise.reject(err)
      }

      const cfg = (err.config ?? {}) as typeof err.config & { suppressGlobalErrorModal?: boolean }

      // Opt-out explícito por request
      if (cfg?.suppressGlobalErrorModal) return Promise.reject(err)

      // Solo mostrar modal en escrituras — las queries GET fallan en silencio
      // para que cada componente decida cómo mostrar el "error al cargar X".
      const method = (cfg?.method ?? 'get').toLowerCase()
      const isWrite = method !== 'get'
      if (!isWrite) return Promise.reject(err)

      // No mostrar si el caller usa código de negocio con UI custom
      const code = err.response?.data?.error
      if (typeof code === 'string' && SKIP_ERROR_CODES.has(code)) {
        return Promise.reject(err)
      }

      // Mostrar modal para errores 400/409/422/500+
      if (status && (status === 400 || status === 409 || status === 422 || status === 403 || status >= 500)) {
        const msg = extractApiMessage(err.response?.data)
          ?? (err.message ? `Error de red: ${err.message}` : `HTTP ${status} — error inesperado.`)

        // Marcar para que onError locales puedan saltarse su propio dialog
        ;(err as AxiosError & { handledByGlobalDialog?: boolean }).handledByGlobalDialog = true

        const detail = err.response?.data && typeof err.response.data === 'object'
          ? JSON.stringify(err.response.data, null, 2)
          : undefined

        // No bloquear el reject — disparamos la modal en paralelo
        void alertDialog({
          kind: status >= 500 ? 'error' : (status === 409 ? 'warning' : 'error'),
          title: titleForStatus(status),
          description: msg,
          detail,
        })
      } else if (!err.response) {
        // Error de red (timeout, sin conexión) en una escritura
        ;(err as AxiosError & { handledByGlobalDialog?: boolean }).handledByGlobalDialog = true
        void alertDialog({
          kind: 'error',
          title: 'Sin conexión',
          description: err.message
            ? `No se pudo contactar al servidor: ${err.message}`
            : 'No se pudo contactar al servidor. Verifica tu conexión e intenta de nuevo.',
        })
      }

      return Promise.reject(err)
    },
  )
}
