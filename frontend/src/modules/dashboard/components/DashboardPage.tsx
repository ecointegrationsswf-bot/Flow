import { MessageSquare, Bot, Megaphone, AlertTriangle } from 'lucide-react'
import { PageHeader } from '@/shared/components/PageHeader'
import { Badge } from '@/shared/components/Badge'
import { StatCard } from './StatCard'
import { useDashboardStats } from '@/shared/hooks/useDashboard'
import { formatDistanceToNow } from 'date-fns'
import { es } from 'date-fns/locale'

const gestionLabels: Record<string, string> = {
  Pending: 'Pendiente',
  PaymentCommitted: 'Compromiso de pago',
  PaymentReceived: 'Pago recibido',
  Rejected: 'Rechazado',
  Rescheduled: 'Reprogramado',
  NoAnswer: 'Sin respuesta',
  EscalatedToHuman: 'Escalado a humano',
}

export function DashboardPage() {
  const { data: stats } = useDashboardStats()

  return (
    <div>
      <PageHeader title="Dashboard" subtitle="Resumen de actividad" />

      {/* KPI Cards */}
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          title="Conversaciones activas"
          value={stats?.totalConversations ?? 0}
          icon={MessageSquare}
          accentColor="bg-blue-100 text-blue-600"
        />
        <StatCard
          title="Agentes activos"
          value={stats?.activeAgents ?? 0}
          icon={Bot}
          accentColor="bg-green-100 text-green-600"
        />
        <StatCard
          title="Campanas activas"
          value={stats?.activeCampaigns ?? 0}
          icon={Megaphone}
          accentColor="bg-amber-100 text-amber-600"
        />
        <StatCard
          title="Escaladas a humano"
          value={stats?.escalatedCount ?? 0}
          icon={AlertTriangle}
          accentColor="bg-red-100 text-red-600"
        />
      </div>

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Gestion por resultado */}
        <div className="rounded-lg bg-white p-5 shadow-sm">
          <h3 className="mb-4 text-sm font-semibold text-gray-900">Gestion por resultado</h3>
          {stats && Object.keys(stats.gestionByResult).length > 0 ? (
            <div className="space-y-3">
              {Object.entries(stats.gestionByResult).map(([key, count]) => (
                <div key={key} className="flex items-center justify-between">
                  <span className="text-sm text-gray-600">{gestionLabels[key] ?? key}</span>
                  <span className="text-sm font-medium text-gray-900">{count}</span>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-gray-400">Sin datos de gestion disponibles</p>
          )}
        </div>

        {/* Actividad reciente */}
        <div className="rounded-lg bg-white p-5 shadow-sm">
          <h3 className="mb-4 text-sm font-semibold text-gray-900">Actividad reciente</h3>
          {stats && stats.recentConversations.length > 0 ? (
            <div className="space-y-3">
              {stats.recentConversations.slice(0, 5).map((c) => (
                <div key={c.id} className="flex items-center justify-between">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-gray-900">
                      {c.clientName ?? c.clientPhone}
                    </p>
                    <p className="truncate text-xs text-gray-500">{c.lastMessagePreview ?? '—'}</p>
                  </div>
                  <div className="ml-3 flex items-center gap-2">
                    <Badge variant={c.agentType}>{c.agentType}</Badge>
                    <span className="text-xs text-gray-400">
                      {formatDistanceToNow(new Date(c.lastActivityAt), { addSuffix: true, locale: es })}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-gray-400">Sin actividad reciente</p>
          )}
        </div>
      </div>
    </div>
  )
}
