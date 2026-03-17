const variants: Record<string, string> = {
  Cobros: 'bg-amber-100 text-amber-800',
  Reclamos: 'bg-red-100 text-red-800',
  Renovaciones: 'bg-blue-100 text-blue-800',
  General: 'bg-gray-100 text-gray-600',
  humano: 'bg-green-100 text-green-800',
  Admin: 'bg-purple-100 text-purple-800',
  Supervisor: 'bg-indigo-100 text-indigo-800',
  ReadOnly: 'bg-gray-100 text-gray-600',
}

interface BadgeProps {
  variant: string
  children: React.ReactNode
  className?: string
}

export function Badge({ variant, children, className = '' }: BadgeProps) {
  const colors = variants[variant] ?? 'bg-gray-100 text-gray-600'
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${colors} ${className}`}>
      {children}
    </span>
  )
}
