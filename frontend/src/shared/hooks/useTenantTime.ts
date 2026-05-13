import { useTenant } from './useTenant'

/**
 * ⚠️ ESTE HOOK ES LA ÚNICA FORMA AUTORIZADA DE FORMATEAR FECHAS/HORAS EN LA UI ⚠️
 *
 * NO uses directamente `new Date(iso).toLocaleString(...)`, `format(new Date, ...)`,
 * ni `new Intl.DateTimeFormat(...)`: todas esas alternativas leen el TZ del navegador
 * (o requieren que cada llamador hardcodee 'America/Panama'), y producen desfases
 * cuando el tenant migra a otro TZ o el usuario abre la app desde un equipo en otro
 * huso. Si necesitás un formato que no exponemos acá, agregalo a este hook (o a
 * `shared/utils/tenantTime.ts` si no estás dentro de un componente React) — no lo
 * inlinees en la pantalla.
 *
 * Convenciones:
 *   - Toda fecha del backend viene en ISO 8601 UTC; el formateo a TZ del tenant
 *     ocurre exclusivamente en este punto.
 *   - `Tenant.TimeZone` (ej: "America/Panama") manda; si está nulo o malformado,
 *     fallback silencioso a "America/Panama".
 */
const FALLBACK_TZ = 'America/Panama'

function safeTz(raw: string | null | undefined): string {
  if (!raw || raw.trim() === '') return FALLBACK_TZ
  // Validamos que el TZ sea legible por Intl. Si no, fallback.
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: raw }).format(new Date())
    return raw
  } catch {
    if (typeof console !== 'undefined') {
      console.warn(`[useTenantTime] TZ inválido del tenant: "${raw}". Cae a ${FALLBACK_TZ}.`)
    }
    return FALLBACK_TZ
  }
}

/**
 * Parsea un valor ISO/Date asegurando interpretación UTC.
 *
 * El backend (.NET) serializa DateTime en formato "2026-05-06T12:00:00.123" SIN
 * el sufijo "Z" — y JavaScript interpreta strings sin TZ como HORA LOCAL del
 * navegador. Eso provocaba que las conversiones con `timeZone: 'America/Panama'`
 * no hicieran nada (si el browser ya estaba en PA) o produjeran offsets dobles.
 * Si el string no trae Z ni offset (+/-HH:mm), agregamos Z antes de parsear.
 */
function toUtcDate(iso: string | Date): Date {
  if (iso instanceof Date) return iso
  // Si ya tiene Z, +HH:mm o -HH:mm (offset explícito), trust it.
  // Detectamos: 'Z' al final, o un signo +/- en posición de TZ tras la T.
  if (/Z$|[+-]\d{2}:?\d{2}$/.test(iso)) return new Date(iso)
  // Si es formato ISO sin TZ, asumimos UTC (convención del backend .NET).
  if (/^\d{4}-\d{2}-\d{2}T/.test(iso)) return new Date(iso + 'Z')
  // Otros formatos (date-only, etc.): parse normal.
  return new Date(iso)
}

/** Etiqueta humana del TZ — ej: "GMT-5 · Panamá". Para el badge del header. */
function describeTz(tz: string): { offset: string; city: string } {
  // Calculamos offset actual real (resuelve DST automáticamente).
  const now = new Date()
  const utc = now.getTime() + now.getTimezoneOffset() * 60000
  let offsetMin = 0
  try {
    const dtf = new Intl.DateTimeFormat('en-US', {
      timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', hour12: false,
    })
    const parts = dtf.formatToParts(new Date(utc))
    const get = (t: string) => Number(parts.find(p => p.type === t)?.value)
    const local = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'))
    offsetMin = Math.round((local - utc) / 60000)
  } catch {
    offsetMin = -300 // -5h default
  }
  const sign = offsetMin >= 0 ? '+' : '-'
  const abs = Math.abs(offsetMin)
  const hh = Math.floor(abs / 60)
  const mm = abs % 60
  const offset = mm === 0 ? `GMT${sign}${hh}` : `GMT${sign}${hh}:${String(mm).padStart(2, '0')}`
  const city = tz.split('/').pop()?.replace(/_/g, ' ') ?? tz
  return { offset, city }
}

export function useTenantTime() {
  const { data: tenant } = useTenant()
  const tz = safeTz(tenant?.timeZone)

  return {
    timeZone: tz,
    label: describeTz(tz),

    /** "3:45 p. m." */
    time: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        hour: 'numeric', minute: '2-digit', hour12: true,
      }).format(toUtcDate(iso))
    },

    /** "01/05/2026" — fecha corta (numérica). */
    date: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        day: '2-digit', month: '2-digit', year: 'numeric',
      }).format(toUtcDate(iso))
    },

    /** "06/05" — fecha sin año, para tablas compactas (Descargas). */
    dateShort: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        day: '2-digit', month: '2-digit',
      }).format(toUtcDate(iso))
    },

    /** "5 de mayo de 2026" — para perfiles, tarjetas. */
    dateLong: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        day: 'numeric', month: 'long', year: 'numeric',
      }).format(toUtcDate(iso))
    },

    /** "01/05/2026 15:23" */
    dateTime: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        day: '2-digit', month: '2-digit', year: 'numeric',
        hour: '2-digit', minute: '2-digit', hour12: false,
      }).format(toUtcDate(iso))
    },

    /** "06/05, 12:57" — fecha+hora compacta (tablas). */
    dateTimeShort: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      const date = new Intl.DateTimeFormat('es-PA', {
        timeZone: tz, day: '2-digit', month: '2-digit',
      }).format(toUtcDate(iso))
      const time = new Intl.DateTimeFormat('es-PA', {
        timeZone: tz, hour: '2-digit', minute: '2-digit', hour12: false,
      }).format(toUtcDate(iso))
      return `${date}, ${time}`
    },

    /** "may. 2026" — para selectores de período. */
    monthYear: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz, month: 'short', year: 'numeric',
      }).format(toUtcDate(iso))
    },

    /**
     * "hace 5 min", "hace 2 h", "hace 3 d". Es relativo al `now` del navegador
     * — no requiere TZ del tenant porque la diferencia temporal es absoluta.
     */
    relative: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      const date = toUtcDate(iso)
      const diffMs = Date.now() - date.getTime()
      const diffMin = Math.floor(diffMs / 60000)
      if (diffMin < 1) return 'ahora'
      if (diffMin < 60) return `hace ${diffMin} min`
      const diffH = Math.floor(diffMin / 60)
      if (diffH < 24) return `hace ${diffH} h`
      const diffD = Math.floor(diffH / 24)
      if (diffD < 30) return `hace ${diffD} d`
      const diffMo = Math.floor(diffD / 30)
      if (diffMo < 12) return `hace ${diffMo} mes${diffMo > 1 ? 'es' : ''}`
      const diffY = Math.floor(diffD / 365)
      return `hace ${diffY} año${diffY > 1 ? 's' : ''}`
    },

    /** Detecta si el ISO es del mismo día calendario en el TZ del tenant. */
    isToday: (iso: string | Date | null | undefined) => {
      if (!iso) return false
      const fmt = new Intl.DateTimeFormat('es-PA', {
        timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit',
      })
      return fmt.format(toUtcDate(iso)) === fmt.format(new Date())
    },

    /** Detecta si fue ayer en el TZ del tenant. */
    isYesterday: (iso: string | Date | null | undefined) => {
      if (!iso) return false
      const fmt = new Intl.DateTimeFormat('es-PA', {
        timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit',
      })
      const yest = new Date()
      yest.setDate(yest.getDate() - 1)
      return fmt.format(toUtcDate(iso)) === fmt.format(yest)
    },
  }
}
