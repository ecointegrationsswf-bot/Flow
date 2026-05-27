import { Link } from 'react-router-dom'
import { FileBarChart, FileText, FileSpreadsheet, ArrowRight, Sparkles } from 'lucide-react'

/**
 * Hub de informes. Muestra los reportes disponibles como tarjetas. El usuario
 * elige cuál generar y navega a la página específica.
 *
 * Diseño: cada reporte vive en su propia ruta para que filtros, estado y PDF
 * sean independientes — el hub es solo el "índice".
 *
 * Nuevos reportes futuros (renovaciones, NPS, etc.) se agregan como una card
 * más en este array, sin tocar layout ni navegación.
 */
type ReportCard = {
  to: string
  title: string
  description: string
  bullets: string[]
  icon: React.ComponentType<{ className?: string }>
  accentClass: string  // tailwind clase con color del header de la card
  cta: string
}

const REPORTS: ReportCard[] = [
  {
    to: '/dashboard/management-report',
    title: 'Informe Gerencial',
    description:
      'Resumen comparativo de gestión por período (mensual o quincenal). ' +
      'Muestra evolución de efectividad y distribución por etiquetas.',
    bullets: [
      'Comparación entre períodos',
      'Gráfico apilado de etiquetas por mes/quincena',
      'Tabla cruzada períodos × clasificaciones',
      'Exporta a Excel (4 hojas) o PDF nativo',
    ],
    icon: FileText,
    accentClass: 'from-blue-500 to-indigo-600',
    cta: 'Generar informe gerencial',
  },
  {
    to: '/reports/effectiveness',
    title: 'Informe de Efectividad',
    description:
      'Reporte por cliente único (no por mensaje-evento). Mide cuántas personas ' +
      'reales terminaron en estado positivo, no cuántos mensajes salieron.',
    bullets: [
      'Métricas por cliente único (teléfono distinto)',
      'Mejor resultado por cliente (rank de etiquetas)',
      'Distribución de # de contactos por cliente',
      'Filtro por maestros de campaña · PDF nativo',
    ],
    icon: FileBarChart,
    accentClass: 'from-emerald-500 to-teal-600',
    cta: 'Generar informe de efectividad',
  },
  {
    to: '/reports/conversation-details',
    title: 'Detalle de Conversaciones',
    description:
      'Excel con el detalle de gestión por conversación — mismo formato que el ' +
      'resumen automático que sale por correo cada mañana, pero a demanda con ' +
      'los filtros que necesites.',
    bullets: [
      'Filtra por rango de fechas',
      'Maestro de campaña opcional (vacío = todos)',
      'Toggle para incluir inbound sin campaña',
      'Columnas: campaña, cliente, etiqueta, resumen, agente',
    ],
    icon: FileSpreadsheet,
    accentClass: 'from-amber-500 to-orange-600',
    cta: 'Generar detalle de conversaciones',
  },
]

export function ReportsPage() {
  return (
    <div className="px-6 py-4">
      <div className="mb-6">
        <h1 className="flex items-center gap-2 text-xl font-bold text-gray-900">
          <Sparkles className="h-5 w-5 text-blue-600" />
          Informes
        </h1>
        <p className="text-sm text-gray-500 mt-1">
          Seleccioná el informe que querés generar. Cada uno tiene sus propios filtros y formato de exportación.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 max-w-5xl">
        {REPORTS.map((r) => {
          const Icon = r.icon
          return (
            <Link
              key={r.to}
              to={r.to}
              className="group relative overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm transition-all hover:shadow-md hover:border-blue-300"
            >
              {/* Banda superior con gradiente del color de acento */}
              <div className={`h-1.5 bg-gradient-to-r ${r.accentClass}`} />

              <div className="p-5">
                <div className="flex items-start gap-3 mb-3">
                  <div className={`rounded-lg bg-gradient-to-br ${r.accentClass} p-2.5 shadow-sm`}>
                    <Icon className="h-5 w-5 text-white" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <h2 className="text-lg font-bold text-gray-900 group-hover:text-blue-700 transition-colors">
                      {r.title}
                    </h2>
                  </div>
                </div>

                <p className="text-sm text-gray-600 mb-3 leading-relaxed">
                  {r.description}
                </p>

                <ul className="space-y-1 mb-4">
                  {r.bullets.map((b) => (
                    <li key={b} className="text-xs text-gray-500 flex items-start gap-1.5">
                      <span className="mt-0.5 inline-block h-1 w-1 rounded-full bg-gray-400 shrink-0" />
                      {b}
                    </li>
                  ))}
                </ul>

                <div className="flex items-center justify-between pt-3 border-t border-gray-100">
                  <span className="text-xs font-medium text-blue-600 group-hover:underline">
                    {r.cta}
                  </span>
                  <ArrowRight className="h-4 w-4 text-blue-600 group-hover:translate-x-0.5 transition-transform" />
                </div>
              </div>
            </Link>
          )
        })}
      </div>
    </div>
  )
}
