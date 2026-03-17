import type { LucideIcon } from 'lucide-react'

interface StatCardProps {
  title: string
  value: string | number
  icon: LucideIcon
  accentColor?: string
}

export function StatCard({ title, value, icon: Icon, accentColor = 'bg-blue-100 text-blue-600' }: StatCardProps) {
  return (
    <div className="rounded-lg bg-white p-5 shadow-sm">
      <div className="flex items-center gap-4">
        <div className={`flex h-10 w-10 items-center justify-center rounded-lg ${accentColor}`}>
          <Icon className="h-5 w-5" />
        </div>
        <div>
          <p className="text-2xl font-semibold text-gray-900">{value}</p>
          <p className="text-sm text-gray-500">{title}</p>
        </div>
      </div>
    </div>
  )
}
