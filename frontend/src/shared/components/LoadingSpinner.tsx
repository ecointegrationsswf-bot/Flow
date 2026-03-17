import { Loader2 } from 'lucide-react'

export function LoadingSpinner() {
  return (
    <div className="flex items-center justify-center p-8">
      <Loader2 className="h-6 w-6 animate-spin text-blue-600" />
    </div>
  )
}
