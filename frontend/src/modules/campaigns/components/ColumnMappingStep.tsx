import { useState } from 'react'
import { ArrowLeft, CheckCircle2, AlertCircle } from 'lucide-react'

interface ColumnMappingStepProps {
  columns: string[]
  previewRows: Record<string, string>[]
  onMappingComplete: (mapping: Record<string, string>) => void
  onBack: () => void
}

const TARGET_FIELDS = [
  { value: '', label: 'No mapear (dato extra)' },
  { value: 'phone', label: 'Telefono' },
  { value: 'email', label: 'Email' },
  { value: 'clientName', label: 'Nombre Cliente' },
  { value: 'policyNumber', label: 'No. Poliza' },
  { value: 'insuranceCompany', label: 'Aseguradora' },
  { value: 'pendingAmount', label: 'Monto Pendiente' },
] as const

export function ColumnMappingStep({
  columns,
  previewRows,
  onMappingComplete,
  onBack,
}: ColumnMappingStepProps) {
  const [mapping, setMapping] = useState<Record<string, string>>(() => {
    const initial: Record<string, string> = {}
    columns.forEach((col) => {
      initial[col] = ''
    })
    return initial
  })

  const phoneIsMapped = Object.values(mapping).includes('phone')

  const handleChange = (column: string, target: string) => {
    setMapping((prev) => {
      const next = { ...prev }
      // If another column already maps to this target, clear it
      if (target) {
        for (const key of Object.keys(next)) {
          if (next[key] === target) {
            next[key] = ''
          }
        }
      }
      next[column] = target
      return next
    })
  }

  const handleConfirm = () => {
    if (!phoneIsMapped) return
    // Build final mapping excluding unmapped columns
    const result: Record<string, string> = {}
    for (const [col, target] of Object.entries(mapping)) {
      if (target) {
        result[col] = target
      }
    }
    onMappingComplete(result)
  }

  const displayRows = previewRows.slice(0, 5)

  return (
    <div className="space-y-6">
      <section className="rounded-lg bg-white p-5 shadow-sm">
        <h2 className="mb-4 text-sm font-semibold text-gray-900">Mapeo de columnas</h2>
        <p className="mb-4 text-sm text-gray-500">
          Asigna cada columna del archivo a un campo del sistema. El campo
          &quot;Telefono&quot; es obligatorio.
        </p>

        <div className="overflow-hidden rounded-md border border-gray-200">
          <table className="w-full text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-2.5 text-left font-medium text-gray-600">
                  Columna del archivo
                </th>
                <th className="px-4 py-2.5 text-left font-medium text-gray-600">
                  Campo destino
                </th>
                <th className="w-8 px-2 py-2.5" />
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {columns.map((col) => (
                <tr key={col} className="hover:bg-gray-50">
                  <td className="px-4 py-2.5 font-medium text-gray-800">{col}</td>
                  <td className="px-4 py-2.5">
                    <select
                      value={mapping[col] || ''}
                      onChange={(e) => handleChange(col, e.target.value)}
                      className="w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                    >
                      {TARGET_FIELDS.map((f) => (
                        <option
                          key={f.value}
                          value={f.value}
                          disabled={
                            f.value !== '' &&
                            f.value !== mapping[col] &&
                            Object.values(mapping).includes(f.value)
                          }
                        >
                          {f.label}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="px-2 py-2.5 text-center">
                    {mapping[col] ? (
                      <CheckCircle2 className="h-4 w-4 text-green-500" />
                    ) : (
                      <span className="text-gray-300">--</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {!phoneIsMapped && (
          <div className="mt-3 flex items-center gap-2 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertCircle className="h-4 w-4 shrink-0" />
            Debes mapear al menos la columna &quot;Telefono&quot; para continuar.
          </div>
        )}
      </section>

      {/* Preview table */}
      {displayRows.length > 0 && (
        <section className="rounded-lg bg-white p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-gray-900">
            Vista previa ({displayRows.length} de {previewRows.length} filas)
          </h2>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-gray-50">
                <tr>
                  {columns.map((col) => (
                    <th
                      key={col}
                      className="whitespace-nowrap px-3 py-2 text-left font-medium text-gray-600"
                    >
                      {col}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {displayRows.map((row, idx) => (
                  <tr key={idx} className="hover:bg-gray-50">
                    {columns.map((col) => (
                      <td
                        key={col}
                        className="whitespace-nowrap px-3 py-2 text-gray-700"
                      >
                        {row[col] ?? ''}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Actions */}
      <div className="flex justify-between">
        <button
          type="button"
          onClick={onBack}
          className="flex items-center gap-1.5 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
        >
          <ArrowLeft className="h-4 w-4" />
          Atras
        </button>
        <button
          type="button"
          onClick={handleConfirm}
          disabled={!phoneIsMapped}
          className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >
          Confirmar mapeo
        </button>
      </div>
    </div>
  )
}
