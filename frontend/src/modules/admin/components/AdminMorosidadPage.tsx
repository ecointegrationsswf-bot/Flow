import { useState, useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'
import {
  TrendingDown, Settings, Map, ChevronDown, Loader2, Save,
  Info, Building2, Zap, History, CheckCircle2, XCircle,
  AlertTriangle, ChevronLeft, ChevronRight, Clock, Download,
} from 'lucide-react'
import { useToast, ToastContainer } from '@/shared/components/Toast'

// ─── Tipos ────────────────────────────────────────────────────────────────────

interface Tenant { id: string; name: string; slug: string; isActive: boolean }
interface TenantAction { id: string; name: string; description?: string; sendsEmail: boolean }
interface LogicalField { id: string; key: string; displayName: string; description?: string; dataType: string; isRequired: boolean }
interface DelinquencyConfig {
  id: string; actionDefinitionId: string; codigoPais: string
  itemsJsonPath: string | null; autoCrearCampanas: boolean
  campaignTemplateId: string | null; agentDefinitionId: string | null
  campaignNamePattern: string | null; notificationEmail: string | null
  downloadWebhookUrl: string | null; downloadWebhookMethod: string
  downloadWebhookHeaders: string | null; isActive: boolean
}
interface ConfigPayload {
  codigoPais: string; itemsJsonPath: string | null; autoCrearCampanas: boolean
  campaignTemplateId: string | null; agentDefinitionId: string | null
  campaignNamePattern: string | null; notificationEmail: string | null
  downloadWebhookUrl: string | null; downloadWebhookMethod: string
  downloadWebhookHeaders: string | null; isActive: boolean
}
type FieldRole = 'None' | 'Phone' | 'ClientName' | 'KeyValue' | 'Amount' | 'PolicyNumber'
interface FieldMapping {
  id?: string
  columnKey: string
  displayName: string
  jsonPath: string
  role: FieldRole
  roleLabel?: string | null
  dataType: string
  sortOrder: number
  defaultValue?: string | null
  isEnabled: boolean
}
interface SimpleItem { id: string; name: string }

// ─── Hooks admin ──────────────────────────────────────────────────────────────

function useTenants() {
  return useQuery<Tenant[]>({
    queryKey: ['admin-tenants'],
    queryFn: async () => {
      const { data } = await adminClient.get<Tenant[]>('/admin/tenants')
      return data
    },
  })
}

function useAdminTenantActions(tenantId: string | null) {
  return useQuery<TenantAction[]>({
    queryKey: ['admin-morosidad-actions', tenantId],
    enabled: !!tenantId,
    queryFn: async () => {
      const { data } = await adminClient.get(`/admin/morosidad/${tenantId}/actions`)
      return data
    },
  })
}

function useAdminFields() {
  return useQuery<LogicalField[]>({
    queryKey: ['admin-morosidad-fields'],
    queryFn: async () => {
      const { data } = await adminClient.get('/admin/morosidad/fields')
      return data
    },
  })
}

function useAdminConfig(tenantId: string | null, actionId: string | null) {
  return useQuery<DelinquencyConfig | null>({
    queryKey: ['admin-morosidad-config', tenantId, actionId],
    enabled: !!tenantId && !!actionId,
    retry: false,
    queryFn: async () => {
      try {
        const { data } = await adminClient.get(`/admin/morosidad/${tenantId}/config/${actionId}`)
        return data
      } catch (e: any) {
        if (e.response?.status === 404) return null
        throw e
      }
    },
  })
}

function useUpsertAdminConfig(tenantId: string, actionId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: ConfigPayload) => {
      await adminClient.put(`/admin/morosidad/${tenantId}/config/${actionId}`, payload)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-morosidad-config', tenantId, actionId] }),
  })
}

function useAdminMappings(actionId: string | null) {
  return useQuery<FieldMapping[]>({
    queryKey: ['admin-morosidad-mappings', actionId],
    enabled: !!actionId,
    queryFn: async () => {
      const { data } = await adminClient.get(`/admin/morosidad/mappings/${actionId}`)
      return data
    },
  })
}

function useSetAdminMappings(actionId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (mappings: FieldMapping[]) => {
      await adminClient.put(`/admin/morosidad/mappings/${actionId}`, { mappings })
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-morosidad-mappings', actionId] }),
  })
}

function useAdminCampaignTemplates(tenantId: string | null) {
  return useQuery<SimpleItem[]>({
    queryKey: ['admin-morosidad-templates', tenantId],
    enabled: !!tenantId,
    queryFn: async () => {
      const { data } = await adminClient.get(`/admin/morosidad/${tenantId}/campaign-templates`)
      return data
    },
  })
}

function useAdminAgents(tenantId: string | null) {
  return useQuery<SimpleItem[]>({
    queryKey: ['admin-morosidad-agents', tenantId],
    enabled: !!tenantId,
    queryFn: async () => {
      const { data } = await adminClient.get(`/admin/morosidad/${tenantId}/agents`)
      return data
    },
  })
}

// ─── Selector genérico ────────────────────────────────────────────────────────

function Selector<T extends { id: string; name: string }>({
  items, selected, onSelect, placeholder, icon: Icon,
}: {
  items: T[]; selected: string | null; onSelect: (id: string) => void
  placeholder: string; icon: React.ElementType
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const filtered = items.filter(i =>
    i.name.toLowerCase().includes(search.toLowerCase()),
  )
  const label = items.find(i => i.id === selected)?.name ?? placeholder

  return (
    <div className="relative">
      <button
        onClick={() => setOpen(o => !o)}
        className="flex w-full items-center justify-between rounded-lg border border-gray-300 bg-white px-4 py-2.5 text-sm shadow-sm hover:bg-gray-50"
      >
        <span className={selected ? 'font-medium text-gray-900' : 'text-gray-400'}>
          {label}
        </span>
        <ChevronDown className={`h-4 w-4 text-gray-400 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      {open && (
        <div className="absolute z-20 mt-1 w-full rounded-lg border border-gray-200 bg-white py-1 shadow-lg">
          <div className="px-3 pb-1 pt-2">
            <input
              autoFocus type="text" value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Buscar..."
              className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
            />
          </div>
          <div className="max-h-48 overflow-y-auto">
            {filtered.length === 0 ? (
              <p className="px-4 py-3 text-sm text-gray-400">Sin resultados</p>
            ) : filtered.map(item => (
              <button
                key={item.id}
                onClick={() => { onSelect(item.id); setOpen(false); setSearch('') }}
                className={`flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm hover:bg-blue-50 ${
                  selected === item.id ? 'bg-blue-50 font-medium text-blue-700' : 'text-gray-700'
                }`}
              >
                <Icon className="h-3.5 w-3.5 shrink-0 text-blue-400" />
                {item.name}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

// ─── Tab Configuración ────────────────────────────────────────────────────────

const COUNTRY_CODES = [
  { code: '507', label: 'Panamá (+507)' },
  { code: '57',  label: 'Colombia (+57)' },
  { code: '52',  label: 'México (+52)' },
  { code: '58',  label: 'Venezuela (+58)' },
  { code: '51',  label: 'Perú (+51)' },
  { code: '593', label: 'Ecuador (+593)' },
  { code: '1',   label: 'EE.UU./Canadá (+1)' },
]

const DEFAULT_CONFIG: ConfigPayload = {
  codigoPais: '507', itemsJsonPath: null, autoCrearCampanas: false,
  campaignTemplateId: null, agentDefinitionId: null,
  campaignNamePattern: '{accion} {fecha}', notificationEmail: null,
  downloadWebhookUrl: null, downloadWebhookMethod: 'GET', downloadWebhookHeaders: null,
  isActive: true,
}

function ConfigTab({ tenantId, actionId }: { tenantId: string; actionId: string }) {
  const { data: config, isLoading } = useAdminConfig(tenantId, actionId)
  const { data: templates = [] } = useAdminCampaignTemplates(tenantId)
  const upsert = useUpsertAdminConfig(tenantId, actionId)

  const [form, setForm] = useState<ConfigPayload>(DEFAULT_CONFIG)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (config) {
      setForm({
        codigoPais:            config.codigoPais || '507',
        itemsJsonPath:         config.itemsJsonPath ?? '',
        autoCrearCampanas:     config.autoCrearCampanas,
        campaignTemplateId:    config.campaignTemplateId,
        agentDefinitionId:     config.agentDefinitionId,
        campaignNamePattern:   config.campaignNamePattern || '{accion} {fecha}',
        notificationEmail:     config.notificationEmail || '',
        downloadWebhookUrl:    config.downloadWebhookUrl || '',
        downloadWebhookMethod: config.downloadWebhookMethod || 'GET',
        downloadWebhookHeaders: config.downloadWebhookHeaders || '',
        isActive:              config.isActive,
      })
    } else if (config === null) {
      setForm(DEFAULT_CONFIG)
    }
  }, [config])

  const handleSave = async () => {
    await upsert.mutateAsync({
      ...form,
      itemsJsonPath:          (form.itemsJsonPath as string)?.trim() || null,
      notificationEmail:      (form.notificationEmail as string)?.trim() || null,
      campaignNamePattern:    (form.campaignNamePattern as string)?.trim() || null,
      downloadWebhookUrl:     (form.downloadWebhookUrl as string)?.trim() || null,
      downloadWebhookHeaders: (form.downloadWebhookHeaders as string)?.trim() || null,
    })
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  if (isLoading) return <div className="flex justify-center py-12"><Loader2 className="h-6 w-6 animate-spin text-gray-400" /></div>

  return (
    <div className="space-y-6">
      {!config && (
        <div className="flex items-start gap-2 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-700">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          <p>Esta acción no tiene configuración de morosidad todavía. Completa el formulario para activarla.</p>
        </div>
      )}

      <div className="grid grid-cols-2 gap-6">
        <div className="space-y-1.5">
          <label className="block text-sm font-medium text-gray-700">Código de país <span className="text-red-500">*</span></label>
          <select
            value={form.codigoPais}
            onChange={e => setForm({ ...form, codigoPais: e.target.value })}
            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none"
          >
            {COUNTRY_CODES.map(c => <option key={c.code} value={c.code}>{c.label}</option>)}
          </select>
          <p className="text-xs text-gray-500">Se agrega como prefijo si el teléfono del payload no lo incluye.</p>
        </div>

        <div className="space-y-1.5">
          <label className="block text-sm font-medium text-gray-700">Items JSON Path</label>
          <input
            type="text"
            value={(form.itemsJsonPath as string) ?? ''}
            onChange={e => setForm({ ...form, itemsJsonPath: e.target.value })}
            placeholder="$.data  o  $.resultado.items"
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none"
          />
          <p className="text-xs text-gray-500">Ruta al array de registros. Vacío = la raíz es el array.</p>
        </div>
      </div>

      {/* ── Endpoint de descarga ──────────────────────────────────────────── */}
      <div className="rounded-lg border border-amber-100 bg-amber-50 p-4 space-y-4">
        <p className="text-xs font-semibold uppercase tracking-wide text-amber-700">
          Endpoint de descarga (Scheduled Job)
        </p>
        <p className="text-xs text-amber-600">
          URL a la que el Worker llama cuando el job se dispara. Si se deja vacío usa el contrato webhook global de la acción.
        </p>
        <div className="grid grid-cols-4 gap-3">
          <div className="space-y-1.5">
            <label className="block text-xs font-medium text-gray-700">Método</label>
            <select
              value={form.downloadWebhookMethod}
              onChange={e => setForm({ ...form, downloadWebhookMethod: e.target.value })}
              className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm"
            >
              <option value="GET">GET</option>
              <option value="POST">POST</option>
            </select>
          </div>
          <div className="col-span-3 space-y-1.5">
            <label className="block text-xs font-medium text-gray-700">URL del endpoint</label>
            <input
              type="url"
              value={(form.downloadWebhookUrl as string) ?? ''}
              onChange={e => setForm({ ...form, downloadWebhookUrl: e.target.value })}
              placeholder="https://api.tobroker.com/morosidad/export"
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-blue-500 focus:outline-none"
            />
          </div>
        </div>
        <div className="space-y-1.5">
          <label className="block text-xs font-medium text-gray-700">Headers (JSON opcional)</label>
          <input
            type="text"
            value={(form.downloadWebhookHeaders as string) ?? ''}
            onChange={e => setForm({ ...form, downloadWebhookHeaders: e.target.value })}
            placeholder={'{"Authorization":"Bearer token","X-ApiKey":"abc"}'}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-blue-500 focus:outline-none"
          />
        </div>
      </div>

      <div className="rounded-lg border border-gray-200 p-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-gray-800">Crear campañas automáticamente</p>
            <p className="mt-0.5 text-xs text-gray-500">Crea una campaña de WhatsApp por cada grupo de contacto al terminar de procesar el payload.</p>
          </div>
          <button
            type="button"
            onClick={() => setForm({ ...form, autoCrearCampanas: !form.autoCrearCampanas })}
            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${form.autoCrearCampanas ? 'bg-blue-600' : 'bg-gray-300'}`}
          >
            <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${form.autoCrearCampanas ? 'translate-x-6' : 'translate-x-1'}`} />
          </button>
        </div>

        {form.autoCrearCampanas && (
          <div className="mt-4 space-y-4 border-t border-gray-100 pt-4">
            <div className="space-y-1.5">
              <label className="block text-sm font-medium text-gray-700">Maestro de campaña <span className="text-red-500">*</span></label>
              <select
                value={form.campaignTemplateId ?? ''}
                onChange={e => setForm({ ...form, campaignTemplateId: e.target.value || null })}
                className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm"
              >
                <option value="">— Seleccionar —</option>
                {templates.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
              </select>
              <p className="text-xs text-gray-500">El agente IA se hereda del maestro de campaña.</p>
            </div>
            <div className="space-y-1.5">
              <label className="block text-sm font-medium text-gray-700">Patrón de nombre</label>
              <input
                type="text"
                value={(form.campaignNamePattern as string) ?? ''}
                onChange={e => setForm({ ...form, campaignNamePattern: e.target.value })}
                placeholder="{accion} {fecha}"
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
              <p className="text-xs text-gray-500">Placeholders: <code className="rounded bg-gray-100 px-1">{'{accion}'}</code> <code className="rounded bg-gray-100 px-1">{'{fecha}'}</code> <code className="rounded bg-gray-100 px-1">{'{grupos}'}</code></p>
            </div>
          </div>
        )}
      </div>

      {!form.autoCrearCampanas && (
        <div className="space-y-1.5">
          <label className="block text-sm font-medium text-gray-700">Email de notificación (modo manual)</label>
          <input
            type="email"
            value={(form.notificationEmail as string) ?? ''}
            onChange={e => setForm({ ...form, notificationEmail: e.target.value })}
            placeholder="ejecutivo@empresa.com"
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
          />
          <p className="text-xs text-gray-500">Recibirá un email cuando el sistema genere nuevos grupos de contacto para revisión.</p>
        </div>
      )}

      <div className="flex items-center gap-3">
        <input
          id="isActive" type="checkbox" checked={form.isActive}
          onChange={e => setForm({ ...form, isActive: e.target.checked })}
          className="h-4 w-4 rounded border-gray-300 text-blue-600"
        />
        <label htmlFor="isActive" className="text-sm text-gray-700">
          Configuración activa (el procesador usará esta config al recibir datos)
        </label>
      </div>

      <div className="flex items-center gap-3 border-t border-gray-100 pt-4">
        <button
          onClick={handleSave}
          disabled={upsert.isPending}
          className="flex items-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-60"
        >
          {upsert.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
          Guardar configuración
        </button>
        {saved && <span className="text-sm font-medium text-green-600">✓ Guardado</span>}
        {upsert.isError && <span className="text-sm text-red-600">Error al guardar</span>}
      </div>
    </div>
  )
}

// ─── Tab Mapeo de campos ──────────────────────────────────────────────────────

const ROLE_OPTIONS: { value: FieldRole; label: string; required: boolean }[] = [
  { value: 'None',         label: 'Sin rol (extra)',     required: false },
  { value: 'Phone',        label: 'Teléfono *',          required: true  },
  { value: 'ClientName',   label: 'Nombre cliente *',    required: true  },
  { value: 'KeyValue',     label: 'KeyValue *',          required: true  },
  { value: 'Amount',       label: 'Monto',               required: false },
  { value: 'PolicyNumber', label: 'Número de póliza',    required: false },
]

const DATA_TYPES = ['string', 'number', 'phone', 'currency', 'date'] as const

function emptyRow(idx: number): FieldMapping {
  return {
    columnKey: '',
    displayName: '',
    jsonPath: '',
    role: 'None',
    roleLabel: null,
    dataType: 'string',
    sortOrder: idx,
    defaultValue: '',
    isEnabled: true,
  }
}

function validateRows(rows: FieldMapping[]): string | null {
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

function MappingsTab({ actionId }: { actionId: string }) {
  const { data: savedMappings = [], isLoading: loadingMappings } = useAdminMappings(actionId)
  const setMappings = useSetAdminMappings(actionId)

  const [rows, setRows] = useState<FieldMapping[]>([])
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (loadingMappings) return
    if (savedMappings.length > 0) {
      setRows(savedMappings.map(m => ({
        id: m.id, columnKey: m.columnKey, displayName: m.displayName,
        jsonPath: m.jsonPath, role: m.role as FieldRole, roleLabel: m.roleLabel,
        dataType: m.dataType, sortOrder: m.sortOrder,
        defaultValue: m.defaultValue ?? '', isEnabled: m.isEnabled,
      })))
    } else {
      // Plantilla mínima para acción nueva — los 3 obligatorios + Amount
      setRows([
        { columnKey: 'PhoneNumber',  displayName: 'Teléfono',           jsonPath: '$.Celular',     role: 'Phone',      roleLabel: null,                    dataType: 'phone',    sortOrder: 0, defaultValue: '', isEnabled: true },
        { columnKey: 'ClientName',   displayName: 'Nombre del cliente', jsonPath: '$.NombreCliente', role: 'ClientName', roleLabel: null,                  dataType: 'string',   sortOrder: 1, defaultValue: '', isEnabled: true },
        { columnKey: 'KeyValue',     displayName: 'Número de póliza',   jsonPath: '$.NroPoliza',   role: 'KeyValue',   roleLabel: 'Número de póliza',     dataType: 'string',   sortOrder: 2, defaultValue: '', isEnabled: true },
        { columnKey: 'Amount',       displayName: 'Saldo pendiente',    jsonPath: '$.Saldo',       role: 'Amount',     roleLabel: null,                    dataType: 'currency', sortOrder: 3, defaultValue: '', isEnabled: true },
      ])
    }
  }, [savedMappings, loadingMappings])

  const updateRow = (idx: number, patch: Partial<FieldMapping>) =>
    setRows(rows.map((r, i) => i === idx ? { ...r, ...patch } : r))

  const removeRow = (idx: number) =>
    setRows(rows.filter((_, i) => i !== idx).map((r, i) => ({ ...r, sortOrder: i })))

  const addRow = () =>
    setRows([...rows, emptyRow(rows.length)])

  const handleSave = async () => {
    const err = validateRows(rows)
    if (err) { setError(err); return }
    setError(null)
    try {
      await setMappings.mutateAsync(rows.map((r, idx) => ({
        columnKey:    r.columnKey.trim(),
        displayName:  r.displayName.trim(),
        jsonPath:     r.jsonPath.trim(),
        role:         r.role,
        roleLabel:    r.roleLabel?.trim() || null,
        dataType:     r.dataType,
        sortOrder:    idx,
        defaultValue: r.defaultValue?.toString().trim() || null,
        isEnabled:    r.isEnabled,
      })))
      setSaved(true)
      setTimeout(() => setSaved(false), 2000)
    } catch (e: any) {
      setError(e?.response?.data?.error ?? 'Error al guardar')
    }
  }

  if (loadingMappings) return <div className="flex justify-center py-12"><Loader2 className="h-6 w-6 animate-spin text-gray-400" /></div>

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-blue-100 bg-blue-50 px-4 py-3 text-xs text-blue-700">
        <strong>Definición de columnas</strong>: cada fila es una columna del JSON de respuesta del webhook.
        Los roles <code className="rounded bg-blue-100 px-1">Phone</code>, <code className="rounded bg-blue-100 px-1">ClientName</code> y <code className="rounded bg-blue-100 px-1">KeyValue</code> son obligatorios.
        El KeyValue requiere etiqueta (ej: "Número de póliza", "Cédula").
      </div>

      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-200 text-left text-xs font-semibold uppercase tracking-wide text-gray-500">
            <th className="pb-2 pr-2 w-8"></th>
            <th className="pb-2 pr-3">Nombre visible</th>
            <th className="pb-2 pr-3">JsonPath</th>
            <th className="pb-2 pr-3">Rol</th>
            <th className="pb-2 pr-3">Etiqueta KeyValue</th>
            <th className="pb-2 pr-3">Tipo</th>
            <th className="pb-2 pr-3">Default</th>
            <th className="pb-2 w-8"></th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {rows.map((row, idx) => (
            <tr key={idx} className={row.isEnabled ? '' : 'opacity-40'}>
              <td className="py-3 pr-2">
                <input
                  type="checkbox" checked={row.isEnabled}
                  onChange={e => updateRow(idx, { isEnabled: e.target.checked })}
                  className="h-4 w-4 rounded border-gray-300 text-blue-600"
                />
              </td>
              <td className="py-3 pr-3">
                <input
                  type="text" value={row.displayName}
                  onChange={e => updateRow(idx, { displayName: e.target.value })}
                  placeholder="ej: Teléfono"
                  className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
                />
              </td>
              <td className="py-3 pr-3">
                <input
                  type="text" value={row.jsonPath}
                  onChange={e => updateRow(idx, { jsonPath: e.target.value })}
                  placeholder="$.campo"
                  className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 font-mono text-xs focus:border-blue-500 focus:outline-none"
                />
              </td>
              <td className="py-3 pr-3">
                <select
                  value={row.role}
                  onChange={e => updateRow(idx, { role: e.target.value as FieldRole })}
                  className="rounded-md border border-gray-300 px-2 py-1.5 text-xs focus:border-blue-500 focus:outline-none"
                >
                  {ROLE_OPTIONS.map(opt => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </td>
              <td className="py-3 pr-3">
                {row.role === 'KeyValue' ? (
                  <input
                    type="text" value={row.roleLabel ?? ''}
                    onChange={e => updateRow(idx, { roleLabel: e.target.value })}
                    placeholder="Número de póliza"
                    className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
                  />
                ) : <span className="text-xs text-gray-300">—</span>}
              </td>
              <td className="py-3 pr-3">
                <select
                  value={row.dataType}
                  onChange={e => updateRow(idx, { dataType: e.target.value })}
                  className="rounded-md border border-gray-300 px-2 py-1.5 text-xs focus:border-blue-500 focus:outline-none"
                >
                  {DATA_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                </select>
              </td>
              <td className="py-3 pr-3">
                <input
                  type="text" value={row.defaultValue ?? ''}
                  onChange={e => updateRow(idx, { defaultValue: e.target.value })}
                  placeholder="(vacío)"
                  className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
                />
              </td>
              <td className="py-3 text-center">
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
        {saved && <span className="text-sm font-medium text-green-600">✓ Guardado</span>}
      </div>
    </div>
  )
}

// ─── Historial de ejecuciones ─────────────────────────────────────────────────

interface Execution {
  id: string; actionDefinitionId: string; status: string
  startedAt: string; completedAt: string | null
  totalItems: number; processedItems: number; discardedItems: number
  groupsCreated: number; campaignsCreated: number; errorMessage: string | null
}
interface ContactGroup {
  id: string; phoneNormalized: string; clientName: string | null
  totalAmount: number; itemCount: number; status: string
  campaignId: string | null; createdAt: string
}
interface DelinquencyItemDetail {
  id: string; rowIndex: number; policyNumber: string | null
  amount: number | null; clientName: string | null
  phoneRaw: string | null; phoneNormalized: string | null
  status: string; discardReason: string | null
}

function useAdminExecutions(tenantId: string | null, actionId: string | null, page: number) {
  return useQuery<{ total: number; page: number; pageSize: number; items: Execution[] }>({
    queryKey: ['admin-executions', tenantId, actionId, page],
    enabled: !!tenantId && !!actionId,
    queryFn: async () => {
      const { data } = await adminClient.get(
        `/admin/morosidad/${tenantId}/executions?actionId=${actionId}&page=${page}&pageSize=10`
      )
      return data
    },
  })
}

function useAdminGroups(tenantId: string, executionId: string, page: number, search: string) {
  return useQuery<{ total: number; page: number; pageSize: number; groups: ContactGroup[] }>({
    queryKey: ['admin-execution-groups', tenantId, executionId, page, search],
    queryFn: async () => {
      const params = new URLSearchParams({ page: String(page), pageSize: '20' })
      if (search) params.set('search', search)
      const { data } = await adminClient.get(
        `/admin/morosidad/${tenantId}/executions/${executionId}/groups?${params}`
      )
      return data
    },
  })
}

function useAdminGroupItems(tenantId: string, executionId: string, groupId: string | null) {
  return useQuery<DelinquencyItemDetail[]>({
    queryKey: ['admin-group-items', tenantId, executionId, groupId],
    enabled: !!groupId,
    queryFn: async () => {
      const { data } = await adminClient.get(
        `/admin/morosidad/${tenantId}/executions/${executionId}/groups/${groupId}/items`
      )
      return data
    },
  })
}

// Badge reutilizable
function StatusBadge({ status }: { status: string }) {
  const map: Record<string, { icon: typeof CheckCircle2; cls: string; label: string }> = {
    Completed:       { icon: CheckCircle2,  cls: 'text-green-600 bg-green-50 border-green-200',  label: 'Completado' },
    PartiallyFailed: { icon: AlertTriangle, cls: 'text-amber-600 bg-amber-50 border-amber-200',  label: 'Parcial' },
    Failed:          { icon: XCircle,       cls: 'text-red-600 bg-red-50 border-red-200',        label: 'Fallido' },
    Running:         { icon: Loader2,       cls: 'text-blue-600 bg-blue-50 border-blue-200',     label: 'Ejecutando' },
    Pending:         { icon: Clock,         cls: 'text-gray-500 bg-gray-50 border-gray-200',     label: 'Pendiente' },
    Processed:       { icon: CheckCircle2,  cls: 'text-green-600 bg-green-50 border-green-200',  label: 'Procesado' },
    Discarded:       { icon: XCircle,       cls: 'text-red-500 bg-red-50 border-red-200',        label: 'Descartado' },
  }
  const { icon: Icon, cls, label } = map[status] ?? { icon: Clock, cls: 'text-gray-500 bg-gray-50 border-gray-200', label: status }
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium ${cls}`}>
      <Icon className="h-3 w-3" /> {label}
    </span>
  )
}

// Panel de detalle de grupos de una ejecución
function ExecutionGroupsPanel({
  tenantId, executionId, onClose,
}: { tenantId: string; executionId: string; onClose: () => void }) {
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [expandedGroup, setExpandedGroup] = useState<string | null>(null)
  const [downloading, setDownloading] = useState(false)
  const { toasts, remove, toast } = useToast()
  const { data, isLoading, isFetching } = useAdminGroups(tenantId, executionId, page, search)
  const { data: items, isLoading: loadingItems } = useAdminGroupItems(tenantId, executionId, expandedGroup)

  const handleExport = async () => {
    setDownloading(true)
    try {
      const token = localStorage.getItem('sa_token')
      const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '/api') as string
      const url = `${baseUrl}/admin/morosidad/${tenantId}/executions/${executionId}/export`
      const resp = await fetch(url, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      })
      if (!resp.ok) throw new Error(`Error ${resp.status}`)
      const blob = await resp.blob()
      const objectUrl = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = objectUrl
      // Obtener nombre del archivo del header Content-Disposition si viene
      const disposition = resp.headers.get('content-disposition')
      const match = disposition?.match(/filename="?([^"]+)"?/)
      a.download = match?.[1] ?? `morosidad_${executionId.slice(0, 8)}.xlsx`
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(objectUrl)
      toast.success('Archivo descargado correctamente.')
    } catch (e) {
      console.error('[Export] Error:', e)
      toast.error('Error al exportar. Verifica la consola.')
    } finally {
      setDownloading(false)
    }
  }

  const fmt = (n: number) => n.toLocaleString('es-PA', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  const totalPages = Math.ceil((data?.total ?? 0) / 20)

  return (
    <div className="mt-2 rounded-lg border border-blue-200 bg-blue-50">
      {/* Header del panel */}
      <div className="flex items-center justify-between border-b border-blue-200 px-4 py-3">
        <div className="flex items-center gap-3">
          <span className="text-sm font-semibold text-blue-800">
            Grupos de contacto
          </span>
          {data && (
            <span className="rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-700">
              {data.total.toLocaleString()} grupos
            </span>
          )}
          {isFetching && <Loader2 className="h-3.5 w-3.5 animate-spin text-blue-400" />}
        </div>
        <div className="flex items-center gap-2">
          <input
            type="text"
            value={searchInput}
            onChange={e => setSearchInput(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') { setSearch(searchInput); setPage(1) } }}
            placeholder="Buscar nombre o teléfono..."
            className="rounded-md border border-blue-300 bg-white px-2.5 py-1 text-xs focus:border-blue-500 focus:outline-none w-52"
          />
          <button
            onClick={handleExport}
            disabled={downloading}
            title="Exportar a Excel (todos los registros con columnas del JSON)"
            className="flex items-center gap-1.5 rounded-md border border-green-300 bg-green-50 px-2.5 py-1 text-xs font-medium text-green-700 hover:bg-green-100 disabled:opacity-50"
          >
            {downloading
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
              : <Download className="h-3.5 w-3.5" />
            }
            Excel
          </button>
          <button onClick={onClose} className="rounded p-1 text-blue-400 hover:bg-blue-100 hover:text-blue-600">
            <XCircle className="h-4 w-4" />
          </button>
        </div>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-8 text-blue-400">
          <Loader2 className="h-5 w-5 animate-spin" />
        </div>
      ) : (data?.groups.length ?? 0) === 0 ? (
        <p className="px-4 py-6 text-center text-sm text-blue-500">Sin grupos registrados para esta ejecución.</p>
      ) : (
        <div className="overflow-hidden">
          <table className="w-full text-xs">
            <thead className="bg-blue-100 text-[10px] uppercase tracking-wide text-blue-600">
              <tr>
                <th className="w-8 px-3 py-2" />
                <th className="px-3 py-2 text-left">Teléfono</th>
                <th className="px-3 py-2 text-left">Cliente</th>
                <th className="px-3 py-2 text-right">Monto total</th>
                <th className="px-3 py-2 text-right">Pólizas</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-left">Campaña</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-blue-100 bg-white">
              {data!.groups.map(g => (
                <>
                  <tr
                    key={g.id}
                    className="cursor-pointer hover:bg-blue-50"
                    onClick={() => setExpandedGroup(expandedGroup === g.id ? null : g.id)}
                  >
                    <td className="px-3 py-2 text-center text-blue-400">
                      <ChevronRight className={`inline h-3.5 w-3.5 transition-transform ${expandedGroup === g.id ? 'rotate-90' : ''}`} />
                    </td>
                    <td className="px-3 py-2 font-mono text-gray-700">{g.phoneNormalized}</td>
                    <td className="px-3 py-2 font-medium text-gray-800">{g.clientName ?? '—'}</td>
                    <td className="px-3 py-2 text-right font-mono text-gray-700">${fmt(g.totalAmount)}</td>
                    <td className="px-3 py-2 text-right text-blue-700 font-medium">{g.itemCount}</td>
                    <td className="px-3 py-2"><StatusBadge status={g.status} /></td>
                    <td className="px-3 py-2">
                      {g.campaignId
                        ? <span className="text-green-600">✓ Creada</span>
                        : <span className="text-gray-400">—</span>
                      }
                    </td>
                  </tr>
                  {expandedGroup === g.id && (
                    <tr key={`${g.id}-items`}>
                      <td colSpan={7} className="bg-gray-50 px-8 py-3">
                        {loadingItems ? (
                          <div className="flex items-center gap-2 text-gray-400">
                            <Loader2 className="h-4 w-4 animate-spin" /> Cargando pólizas...
                          </div>
                        ) : (items?.length ?? 0) === 0 ? (
                          <p className="text-xs text-gray-400">Sin ítems.</p>
                        ) : (
                          <table className="w-full text-xs">
                            <thead className="text-[10px] uppercase tracking-wide text-gray-400">
                              <tr>
                                <th className="pb-1 pr-4 text-left">Póliza</th>
                                <th className="pb-1 pr-4 text-right">Monto</th>
                                <th className="pb-1 pr-4 text-left">Teléfono raw</th>
                                <th className="pb-1 text-left">Estado</th>
                              </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                              {items!.map(item => (
                                <tr key={item.id}>
                                  <td className="py-1 pr-4 font-mono">{item.policyNumber ?? '—'}</td>
                                  <td className="py-1 pr-4 text-right">{item.amount != null ? `$${fmt(item.amount)}` : '—'}</td>
                                  <td className="py-1 pr-4 font-mono text-gray-500">{item.phoneRaw ?? '—'}</td>
                                  <td className="py-1">
                                    <StatusBadge status={item.status} />
                                    {item.discardReason && (
                                      <span className="ml-1 text-[10px] text-red-400">({item.discardReason})</span>
                                    )}
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        )}
                      </td>
                    </tr>
                  )}
                </>
              ))}
            </tbody>
          </table>

          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t border-blue-100 px-4 py-2 text-xs text-blue-600">
              <span>Página {page} de {totalPages} · {data!.total.toLocaleString()} grupos</span>
              <div className="flex gap-1">
                <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}
                  className="rounded border border-blue-200 p-1 disabled:opacity-40 hover:bg-blue-100">
                  <ChevronLeft className="h-3.5 w-3.5" />
                </button>
                <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}
                  className="rounded border border-blue-200 p-1 disabled:opacity-40 hover:bg-blue-100">
                  <ChevronRight className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Toast notifications */}
      <ToastContainer toasts={toasts} onRemove={remove} />
    </div>
  )
}

function HistorialTab({ tenantId, actionId }: { tenantId: string; actionId: string }) {
  const [page, setPage] = useState(1)
  const { data, isLoading, isFetching } = useAdminExecutions(tenantId, actionId, page)
  const [expandedExecution, setExpandedExecution] = useState<string | null>(null)

  const fmt = (iso: string | null) => iso
    ? new Date(iso).toLocaleString('es-PA', { timeZone: 'America/Panama', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false })
    : '—'

  if (isLoading) return <div className="flex items-center gap-2 py-10 text-gray-400"><Loader2 className="h-5 w-5 animate-spin" /> Cargando historial...</div>

  const totalPages = Math.ceil((data?.total ?? 0) / 10)

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-500">{data?.total ?? 0} ejecuciones registradas</p>
        {isFetching && <Loader2 className="h-4 w-4 animate-spin text-gray-400" />}
      </div>

      {(data?.items.length ?? 0) === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 py-12 text-center text-sm text-gray-400">
          Sin ejecuciones aún — el job no ha corrido o no hay configuración activa.
        </div>
      ) : (
        <>
          <div className="overflow-hidden rounded-lg border border-gray-200">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs uppercase tracking-wide text-gray-500">
                <tr>
                  <th className="w-8 px-3 py-2" />
                  <th className="px-4 py-2 text-left">Inicio (Panamá)</th>
                  <th className="px-4 py-2 text-left">Estado</th>
                  <th className="px-4 py-2 text-right">Total</th>
                  <th className="px-4 py-2 text-right">Procesados</th>
                  <th className="px-4 py-2 text-right">Descartados</th>
                  <th className="px-4 py-2 text-right">Grupos</th>
                  <th className="px-4 py-2 text-right">Campañas</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data!.items.map(e => (
                  <>
                    <tr
                      key={e.id}
                      className="cursor-pointer hover:bg-gray-50"
                      onClick={() => setExpandedExecution(expandedExecution === e.id ? null : e.id)}
                    >
                      <td className="px-3 py-2.5 text-center text-gray-400">
                        <ChevronRight className={`inline h-4 w-4 transition-transform ${expandedExecution === e.id ? 'rotate-90' : ''}`} />
                      </td>
                      <td className="px-4 py-2.5 font-mono text-xs text-gray-600">{fmt(e.startedAt)}</td>
                      <td className="px-4 py-2.5"><StatusBadge status={e.status} /></td>
                      <td className="px-4 py-2.5 text-right font-medium">{e.totalItems.toLocaleString()}</td>
                      <td className="px-4 py-2.5 text-right text-green-700">{e.processedItems.toLocaleString()}</td>
                      <td className="px-4 py-2.5 text-right text-amber-600">{e.discardedItems.toLocaleString()}</td>
                      <td className="px-4 py-2.5 text-right text-blue-600 font-semibold">{e.groupsCreated.toLocaleString()}</td>
                      <td className="px-4 py-2.5 text-right text-purple-600">{e.campaignsCreated.toLocaleString()}</td>
                    </tr>
                    {expandedExecution === e.id && (
                      <tr key={`${e.id}-detail`}>
                        <td colSpan={8} className="px-4 pb-4 pt-1">
                          <ExecutionGroupsPanel
                            tenantId={tenantId}
                            executionId={e.id}
                            onClose={() => setExpandedExecution(null)}
                          />
                        </td>
                      </tr>
                    )}
                  </>
                ))}
              </tbody>
            </table>
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-between text-sm text-gray-500">
              <span>Página {page} de {totalPages}</span>
              <div className="flex gap-1">
                <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}
                  className="rounded border border-gray-300 p-1 disabled:opacity-40 hover:bg-gray-100">
                  <ChevronLeft className="h-4 w-4" />
                </button>
                <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}
                  className="rounded border border-gray-300 p-1 disabled:opacity-40 hover:bg-gray-100">
                  <ChevronRight className="h-4 w-4" />
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}

// ─── Página principal ─────────────────────────────────────────────────────────

type Tab = 'config' | 'mappings' | 'history'

const TABS: { id: Tab; icon: typeof Settings; label: string }[] = [
  { id: 'config',   icon: Settings, label: 'Configuración' },
  { id: 'mappings', icon: Map,      label: 'Mapeo de campos' },
  { id: 'history',  icon: History,  label: 'Historial' },
]

export function AdminMorosidadPage() {
  const [searchParams] = useSearchParams()
  const { data: tenants = [], isLoading: loadingTenants } = useTenants()
  const [selectedTenantId, setSelectedTenantId] = useState<string | null>(searchParams.get('tenant'))
  const [selectedActionId, setSelectedActionId] = useState<string | null>(searchParams.get('action'))
  const [tab, setTab] = useState<Tab>('config')

  const { data: actions = [], isLoading: loadingActions } = useAdminTenantActions(selectedTenantId)

  const handleSelectTenant = (id: string) => {
    setSelectedTenantId(id)
    setSelectedActionId(null)
    setTab('config')
  }

  const handleSelectAction = (id: string) => {
    setSelectedActionId(id)
    setTab('config')
  }

  // Adaptar tenants para el Selector
  const tenantItems = tenants.map(t => ({ id: t.id, name: t.name }))
  // Mostrar descripción amigable en lugar del slug crudo (DOWNLOAD_DELINQUENCY_DATA → "Descargar datos de morosidad")
  const actionItems = actions.map(a => ({ id: a.id, name: a.description?.trim() || a.name }))

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold text-gray-900">
          <TrendingDown className="h-6 w-6 text-amber-500" />
          Módulo de Descargas
        </h1>
        <p className="mt-1 text-sm text-gray-500">
          Configura cualquier descarga de datos vía webhook por tenant y acción.
          Las acciones aquí listadas son las marcadas como "Descarga de morosidad" en el catálogo.
        </p>
      </div>

      {/* Selectores */}
      <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
        <div className="grid grid-cols-2 gap-6">
          {/* Tenant */}
          <div>
            <label className="mb-2 block text-sm font-semibold text-gray-700">Tenant</label>
            {loadingTenants ? (
              <div className="flex items-center gap-2 text-sm text-gray-400">
                <Loader2 className="h-4 w-4 animate-spin" /> Cargando tenants...
              </div>
            ) : (
              <Selector
                items={tenantItems}
                selected={selectedTenantId}
                onSelect={handleSelectTenant}
                placeholder="Seleccionar tenant..."
                icon={Building2}
              />
            )}
          </div>

          {/* Acción */}
          <div>
            <label className="mb-2 block text-sm font-semibold text-gray-700">Acción a configurar</label>
            {!selectedTenantId ? (
              <p className="text-sm text-gray-400">Selecciona un tenant primero.</p>
            ) : loadingActions ? (
              <div className="flex items-center gap-2 text-sm text-gray-400">
                <Loader2 className="h-4 w-4 animate-spin" /> Cargando acciones...
              </div>
            ) : actions.length === 0 ? (
              <p className="text-sm text-gray-400">Este tenant no tiene acciones asignadas.</p>
            ) : (
              <Selector
                items={actionItems}
                selected={selectedActionId}
                onSelect={handleSelectAction}
                placeholder="Seleccionar acción..."
                icon={Zap}
              />
            )}
          </div>
        </div>
      </div>

      {/* Panel de configuración */}
      {selectedTenantId && selectedActionId && (
        <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
          {/* Tabs */}
          <div className="border-b border-gray-200">
            <div className="flex px-4">
              {TABS.map(({ id, icon: Icon, label }) => (
                <button
                  key={id}
                  onClick={() => setTab(id)}
                  className={`flex items-center gap-2 border-b-2 px-4 py-3.5 text-sm font-medium transition-colors ${
                    tab === id
                      ? 'border-blue-600 text-blue-600'
                      : 'border-transparent text-gray-500 hover:text-gray-700'
                  }`}
                >
                  <Icon className="h-4 w-4" />
                  {label}
                </button>
              ))}
            </div>
          </div>

          <div className="p-6">
            {tab === 'config' && (
              <ConfigTab tenantId={selectedTenantId} actionId={selectedActionId} />
            )}
            {tab === 'mappings' && (
              <MappingsTab actionId={selectedActionId} />
            )}
            {tab === 'history' && (
              <HistorialTab tenantId={selectedTenantId} actionId={selectedActionId} />
            )}
          </div>
        </div>
      )}
    </div>
  )
}
