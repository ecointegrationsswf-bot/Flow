import { Link } from 'react-router-dom'
import { Megaphone, Plus, Rocket } from 'lucide-react'
import { format } from 'date-fns'
import { PageHeader } from '@/shared/components/PageHeader'
import { Badge } from '@/shared/components/Badge'
import { EmptyState } from '@/shared/components/EmptyState'
import { LoadingSpinner } from '@/shared/components/LoadingSpinner'
import { useCampaigns, useLaunchCampaign } from '@/shared/hooks/useCampaigns'

const triggerLabels: Record<string, string> = {
  FileUpload: 'Archivo',
  PolicyEvent: 'Evento poliza',
  DelinquencyEvent: 'Morosidad',
  Manual: 'Manual',
}

const statusConfig: Record<string, { label: string; className: string }> = {
  Pending:    { label: 'Pendiente',  className: 'bg-gray-100 text-gray-600' },
  Launching:  { label: 'Lanzando',   className: 'bg-yellow-100 text-yellow-700' },
  Running:    { label: 'En curso',   className: 'bg-green-100 text-green-700' },
  Paused:     { label: 'Pausada',    className: 'bg-orange-100 text-orange-700' },
  Completed:  { label: 'Completada', className: 'bg-blue-100 text-blue-700' },
  Failed:     { label: 'Error',      className: 'bg-red-100 text-red-700' },
}

export function CampaignsPage() {
  const { data: campaigns, isLoading, isError } = useCampaigns()
  const launchMut = useLaunchCampaign()

  if (isLoading) return <LoadingSpinner />

  return (
    <div>
      <PageHeader
        title="Campanas"
        subtitle="Gestiona campanas de cobros, reclamos y renovaciones"
        action={
          <Link
            to="/campaigns/new"
            className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
          >
            <Plus className="h-4 w-4" /> Nueva campana
          </Link>
        }
      />

      {isError || !campaigns || campaigns.length === 0 ? (
        <EmptyState
          icon={Megaphone}
          title="Sin campanas"
          description="Crea tu primera campana subiendo un archivo de contactos"
          action={
            <Link
              to="/campaigns/new"
              className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 transition-colors"
            >
              Crear campana
            </Link>
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Nombre</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Tipo</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Canal</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Progreso</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Estado</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Fecha</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {campaigns.map((c) => {
                const pct = c.totalContacts > 0 ? Math.round((c.processedContacts / c.totalContacts) * 100) : 0
                return (
                  <tr key={c.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3">
                      <p className="text-sm font-medium text-gray-900">{c.name}</p>
                      {c.sourceFileName && <p className="text-xs text-gray-500">{c.sourceFileName}</p>}
                    </td>
                    <td className="px-4 py-3">
                      <span className="text-xs text-gray-600">{triggerLabels[c.trigger] ?? c.trigger}</span>
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant="General">{c.channel}</Badge>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <div className="h-2 w-24 overflow-hidden rounded-full bg-gray-200">
                          <div className="h-full rounded-full bg-blue-600" style={{ width: `${pct}%` }} />
                        </div>
                        <span className="text-xs text-gray-500">{c.processedContacts}/{c.totalContacts}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      {(() => {
                        const st = c.status ?? (c.completedAt ? 'Completed' : c.isActive ? 'Running' : 'Pending')
                        const cfg = statusConfig[st] ?? statusConfig.Pending
                        return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${cfg.className}`}>{cfg.label}</span>
                      })()}
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-500">
                      {format(new Date(c.createdAt), 'dd/MM/yyyy')}
                    </td>
                    <td className="px-4 py-3">
                      {(!c.status || c.status === 'Pending') && (
                        <button
                          onClick={() => launchMut.mutate(c.id)}
                          disabled={launchMut.isPending}
                          className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
                        >
                          <Rocket className="h-3.5 w-3.5" />
                          {launchMut.isPending ? 'Lanzando...' : 'Lanzar'}
                        </button>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
