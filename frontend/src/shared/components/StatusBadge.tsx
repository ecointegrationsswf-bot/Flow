import type { ConversationStatus } from '@/shared/types'

const statusConfig: Record<ConversationStatus, { label: string; colors: string }> = {
  Active: { label: 'Activa', colors: 'bg-green-100 text-green-800' },
  WaitingClient: { label: 'Esperando cliente', colors: 'bg-yellow-100 text-yellow-800' },
  EscalatedToHuman: { label: 'Escalada', colors: 'bg-red-100 text-red-800' },
  Closed: { label: 'Cerrada', colors: 'bg-gray-100 text-gray-600' },
  Unresponsive: { label: 'Sin respuesta', colors: 'bg-orange-100 text-orange-800' },
}

export function StatusBadge({ status }: { status: ConversationStatus }) {
  const cfg = statusConfig[status] ?? statusConfig.Active
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${cfg.colors}`}>
      {cfg.label}
    </span>
  )
}
