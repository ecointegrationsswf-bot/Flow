import { useTenant } from './useTenant'

/**
 * Formateo de fechas/horas en la zona horaria configurada en el tenant
 * (Tenant.TimeZone — ej: "America/Panama"). Si el tenant aún no cargó,
 * cae al timezone del navegador como fallback.
 */
export function useTenantTime() {
  const { data: tenant } = useTenant()
  const tz = tenant?.timeZone || undefined

  return {
    timeZone: tz,

    /** "3:45 p. m." */
    time: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        hour: 'numeric', minute: '2-digit', hour12: true,
      }).format(new Date(iso))
    },

    /** "01/05/2026" */
    date: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        day: '2-digit', month: '2-digit', year: 'numeric',
      }).format(new Date(iso))
    },

    /** "01/05/2026 15:23" */
    dateTime: (iso: string | Date | null | undefined) => {
      if (!iso) return ''
      return new Intl.DateTimeFormat('es-PA', {
        timeZone: tz,
        day: '2-digit', month: '2-digit', year: 'numeric',
        hour: '2-digit', minute: '2-digit', hour12: false,
      }).format(new Date(iso))
    },

    /** Detecta si el ISO es del mismo día calendario en el TZ del tenant. */
    isToday: (iso: string | Date | null | undefined) => {
      if (!iso) return false
      const fmt = new Intl.DateTimeFormat('es-PA', {
        timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit',
      })
      return fmt.format(new Date(iso)) === fmt.format(new Date())
    },

    /** Detecta si fue ayer en el TZ del tenant. */
    isYesterday: (iso: string | Date | null | undefined) => {
      if (!iso) return false
      const fmt = new Intl.DateTimeFormat('es-PA', {
        timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit',
      })
      const yest = new Date()
      yest.setDate(yest.getDate() - 1)
      return fmt.format(new Date(iso)) === fmt.format(yest)
    },
  }
}
