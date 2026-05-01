import { useEffect, useState } from 'react'
import { Save, Loader2, CheckCircle2, XCircle } from 'lucide-react'
import {
  useFieldMappings,
  useSetFieldMappings,
  type FieldMapping,
  type FieldMappingPayload,
  type FieldRole,
} from '../hooks/useMorosidad'

interface Props {
  actionId: string
}

const ROLE_OPTIONS: { value: FieldRole; label: string }[] = [
  { value: 'None',         label: 'Sin rol (extra)' },
  { value: 'Phone',        label: 'Teléfono *' },
  { value: 'ClientName',   label: 'Nombre cliente *' },
  { value: 'KeyValue',     label: 'KeyValue *' },
  { value: 'Amount',       label: 'Monto' },
  { value: 'PolicyNumber', label: 'Número de póliza' },
]

const DATA_TYPES = ['string', 'number', 'phone', 'currency', 'date'] as const

interface Row {
  id?: string
  columnKey: string
  displayName: string
  jsonPath: string
  role: FieldRole
  roleLabel: string | null
  dataType: string
  sortOrder: number
  defaultValue: string
  isEnabled: boolean
}

function emptyRow(idx: number): Row {
  return {
    columnKey: '', displayName: '', jsonPath: '',
    role: 'None', roleLabel: null, dataType: 'string',
    sortOrder: idx, defaultValue: '', isEnabled: true,
  }
}

function defaultRows(): Row[] {
  return [
    { columnKey: 'PhoneNumber',  displayName: 'Teléfono',           jsonPath: '$.Celular',     role: 'Phone',      roleLabel: null,                    dataType: 'phone',    sortOrder: 0, defaultValue: '', isEnabled: true },
    { columnKey: 'ClientName',   displayName: 'Nombre del cliente', jsonPath: '$.NombreCliente', role: 'ClientName', roleLabel: null,                  dataType: 'string',   sortOrder: 1, defaultValue: '', isEnabled: true },
    { columnKey: 'KeyValue',     displayName: 'Número de póliza',   jsonPath: '$.NroPoliza',   role: 'KeyValue',   roleLabel: 'Número de póliza',     dataType: 'string',   sortOrder: 2, defaultValue: '', isEnabled: true },
    { columnKey: 'Amount',       displayName: 'Saldo pendiente',    jsonPath: '$.Saldo',       role: 'Amount',     roleLabel: null,                    dataType: 'currency', sortOrder: 3, defaultValue: '', isEnabled: true },
  ]
}

function validate(rows: Row[]): string | null {
  if (rows.length === 0) return 'Debe definir al menos los 3 campos obligatorios.'
  const paths = new Set<string>()
  for (const r of rows) {
    const label = r.displayName.trim() || r.jsonPath.trim() || '(sin nombre)'
    if (!r.displayName.trim()) return `Hay una columna sin nombre visible (JsonPath: "${r.jsonPath}").`
    if (!r.jsonPath.trim()) return `La columna "${label}" no tiene JsonPath.`
    const p = r.jsonPath.trim().toLowerCase()
    if (paths.has(p)) return `Hay dos columnas que apuntan al mismo JsonPath ("${r.jsonPath}").`
    paths.add(p)
  }
  const byRole: Record<string, number> = {}
  for (const r of rows) {
    if (r.role !== 'None') byRole[r.role] = (byRole[r.role] ?? 0) + 1
  }
  for (const required of ['Phone', 'ClientName', 'KeyValue']) {
    if (!byRole[required]) return `Falta marcar el campo con rol ${required}.`
  }
  for (const [role, count] of Object.entries(byRole)) {
    if (count > 1) return `El rol ${role} aparece en ${count} columnas (debe ser único).`
  }
  const kv = rows.find(r => r.role === 'KeyValue')
  if (kv && !(kv.roleLabel ?? '').trim()) return 'KeyValue requiere etiqueta (ej: "Número de póliza").'
  return null
}

export function MorosidadMappingsTab({ actionId }: Props) {
  const { data: existingMappings = [], isLoading } = useFieldMappings(actionId)
  const setMappings = useSetFieldMappings(actionId)

  const [rows, setRows] = useState<Row[]>([])
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (isLoading) return
    if (existingMappings.length > 0) {
      setRows(existingMappings.map((m: FieldMapping) => ({
        id: m.id,
        columnKey: m.columnKey,
        displayName: m.displayName,
        jsonPath: m.jsonPath,
        role: m.role,
        roleLabel: m.roleLabel,
        dataType: m.dataType,
        sortOrder: m.sortOrder,
        defaultValue: m.defaultValue ?? '',
        isEnabled: m.isEnabled,
      })))
    } else {
      setRows(defaultRows())
    }
  }, [existingMappings, isLoading])

  const updateRow = (idx: number, patch: Partial<Row>) =>
    setRows(rows.map((r, i) => i === idx ? { ...r, ...patch } : r))
  const removeRow = (idx: number) =>
    setRows(rows.filter((_, i) => i !== idx).map((r, i) => ({ ...r, sortOrder: i })))
  const addRow = () => setRows([...rows, emptyRow(rows.length)])

  const handleSave = async () => {
    const err = validate(rows)
    if (err) { setError(err); return }
    setError(null)
    try {
      const payload: FieldMappingPayload[] = rows.map((r, idx) => ({
        columnKey:    r.columnKey.trim(),
        displayName:  r.displayName.trim(),
        jsonPath:     r.jsonPath.trim(),
        role:         r.role,
        roleLabel:    r.roleLabel?.trim() || null,
        dataType:     r.dataType,
        sortOrder:    idx,
        defaultValue: r.defaultValue.trim() || null,
        isEnabled:    r.isEnabled,
      }))
      await setMappings.mutateAsync(payload)
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (e: any) {
      setError(e?.response?.data?.error ?? 'Error al guardar')
    }
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="rounded-lg bg-amber-50 border border-amber-200 px-4 py-3 text-sm text-amber-800">
        <p className="font-medium">Definición de columnas</p>
        <p className="mt-1 text-xs">
          Cada fila es una columna del JSON que devuelve el webhook de descarga.
          Los roles <code className="rounded bg-amber-100 px-1">Phone</code>, <code className="rounded bg-amber-100 px-1">ClientName</code> y <code className="rounded bg-amber-100 px-1">KeyValue</code> son obligatorios — sin ellos no se puede iniciar el procesamiento.
          El KeyValue requiere etiqueta (ej: "Número de póliza", "Cédula"); ese identificador es lo que el webhook de gestión usa para ligar la respuesta del cliente con el sistema externo.
        </p>
      </div>

      <div className="overflow-hidden rounded-lg border border-gray-200">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-left text-xs uppercase tracking-wide text-gray-500">
            <tr>
              <th className="px-3 py-2 w-8"></th>
              <th className="px-3 py-2">Nombre visible</th>
              <th className="px-3 py-2">JsonPath</th>
              <th className="px-3 py-2">Rol</th>
              <th className="px-3 py-2">Etiqueta KeyValue</th>
              <th className="px-3 py-2">Tipo</th>
              <th className="px-3 py-2">Default</th>
              <th className="px-3 py-2 w-8"></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {rows.map((row, idx) => (
              <tr key={idx} className={row.isEnabled ? '' : 'opacity-40'}>
                <td className="px-3 py-2.5">
                  <input
                    type="checkbox" checked={row.isEnabled}
                    onChange={e => updateRow(idx, { isEnabled: e.target.checked })}
                    className="h-4 w-4 rounded border-gray-300 text-blue-600"
                  />
                </td>
                <td className="px-3 py-2.5">
                  <input
                    type="text" value={row.displayName}
                    onChange={e => updateRow(idx, { displayName: e.target.value })}
                    placeholder="Teléfono"
                    className="w-full rounded border border-gray-300 px-2 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
                  />
                </td>
                <td className="px-3 py-2.5">
                  <input
                    type="text" value={row.jsonPath}
                    onChange={e => updateRow(idx, { jsonPath: e.target.value })}
                    placeholder="$.campo"
                    className="w-full rounded border border-gray-300 px-2 py-1.5 font-mono text-xs focus:border-blue-500 focus:outline-none"
                  />
                </td>
                <td className="px-3 py-2.5">
                  <select
                    value={row.role}
                    onChange={e => updateRow(idx, { role: e.target.value as FieldRole })}
                    className="rounded border border-gray-300 px-2 py-1.5 text-xs focus:border-blue-500 focus:outline-none"
                  >
                    {ROLE_OPTIONS.map(opt => <option key={opt.value} value={opt.value}>{opt.label}</option>)}
                  </select>
                </td>
                <td className="px-3 py-2.5">
                  {row.role === 'KeyValue' ? (
                    <input
                      type="text" value={row.roleLabel ?? ''}
                      onChange={e => updateRow(idx, { roleLabel: e.target.value })}
                      placeholder="Número de póliza"
                      className="w-full rounded border border-gray-300 px-2 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
                    />
                  ) : <span className="text-xs text-gray-300">—</span>}
                </td>
                <td className="px-3 py-2.5">
                  <select
                    value={row.dataType}
                    onChange={e => updateRow(idx, { dataType: e.target.value })}
                    className="rounded border border-gray-300 px-2 py-1.5 text-xs focus:border-blue-500 focus:outline-none"
                  >
                    {DATA_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                  </select>
                </td>
                <td className="px-3 py-2.5">
                  <input
                    type="text" value={row.defaultValue}
                    onChange={e => updateRow(idx, { defaultValue: e.target.value })}
                    placeholder="(vacío)"
                    className="w-full rounded border border-gray-300 px-2 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
                  />
                </td>
                <td className="px-3 py-2.5 text-center">
                  <button
                    onClick={() => removeRow(idx)}
                    className="rounded p-1 text-gray-400 hover:bg-red-50 hover:text-red-600"
                    title="Eliminar columna"
                  >
                    <XCircle className="h-4 w-4" />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <button
        onClick={addRow}
        className="flex items-center gap-2 rounded-md border border-dashed border-blue-300 px-3 py-2 text-sm text-blue-600 hover:bg-blue-50"
      >
        <span className="text-lg leading-none">+</span> Agregar campo
      </button>

      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="flex items-center gap-3 border-t border-gray-100 pt-4">
        <button
          onClick={handleSave}
          disabled={setMappings.isPending}
          className="flex items-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-60"
        >
          {setMappings.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
          Guardar mapeos
        </button>
        {saved && (
          <span className="flex items-center gap-1.5 text-sm font-medium text-green-600">
            <CheckCircle2 className="h-4 w-4" /> Mapeos guardados
          </span>
        )}
      </div>
    </div>
  )
}
