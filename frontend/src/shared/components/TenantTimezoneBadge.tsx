import { Globe } from 'lucide-react'
import { useTenantTime } from '@/shared/hooks/useTenantTime'

/**
 * Chip pequeño que muestra la zona horaria efectiva del tenant. Sirve para
 * que el usuario vea de un vistazo en qué TZ se están renderizando todas las
 * fechas de la app — útil cuando el cliente abre desde un equipo en otro
 * huso o cuando un tenant nuevo se configura en TZ distinto a Panamá.
 */
export function TenantTimezoneBadge() {
  const tt = useTenantTime()
  return (
    <span
      title={`Todas las horas mostradas están en ${tt.timeZone}. Si necesitás cambiar la zona del tenant, hacelo desde Configuración.`}
      className="inline-flex items-center gap-1 rounded-full bg-blue-50 px-2 py-0.5 text-[11px] font-medium text-blue-700"
    >
      <Globe className="h-3 w-3" />
      {tt.label.city} · {tt.label.offset}
    </span>
  )
}
