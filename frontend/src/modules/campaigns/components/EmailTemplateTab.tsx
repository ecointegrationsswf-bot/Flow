import { useEffect, useMemo, useRef, useState } from 'react'
import { useEditor, EditorContent, type Editor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import Placeholder from '@tiptap/extension-placeholder'
import {
  Eye, Send, Copy as CopyIcon, X, Sparkles,
  Bold, Italic, Underline as UnderlineIcon, List, ListOrdered, Quote,
  Link as LinkIcon, Image as ImageIcon, Heading1, Heading2, Heading3,
  Code, Pencil, AlertTriangle, Upload, CheckCircle2,
} from 'lucide-react'
import { usePreviewEmailTemplate, useTestSendEmailTemplate, useParseEmailSample } from '@/shared/hooks/useCampaignTemplates'
import { SUGGESTED_EMAIL_HTML, SUGGESTED_EMAIL_SUBJECT } from './suggestedEmailTemplate'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { promptDialog } from '@/shared/components/dialog'

type EditorMode = 'visual' | 'html'

/** Detecta HTML "avanzado" que TipTap StarterKit destruiría — tablas, atributos
 *  inline complejos, directivas Scriban con bloques. */
function isComplexHtml(html: string): boolean {
  if (!html) return false
  return /<table|<style|{{\s*(if|for|else|end)\b/i.test(html)
}

// Variables disponibles para insertar en el editor. El orden importa: agrupadas por
// namespace para que el panel lateral se vea ordenado.
const MERGE_TAGS: { tag: string; label: string; group: string }[] = [
  { group: 'Cliente', tag: '{{cliente.nombre}}',      label: 'Nombre' },
  { group: 'Cliente', tag: '{{cliente.telefono}}',    label: 'Teléfono' },
  { group: 'Cliente', tag: '{{cliente.email}}',       label: 'Email' },
  { group: 'Cliente', tag: '{{cliente.poliza}}',      label: 'Póliza' },
  { group: 'Cliente', tag: '{{cliente.aseguradora}}', label: 'Aseguradora' },
  { group: 'Cliente', tag: '{{cliente.saldo}}',       label: 'Saldo' },
  { group: 'Cliente', tag: '{{cliente.datos.XXX}}',   label: 'Dato extra del archivo' },

  { group: 'Conversación', tag: '{{conversacion.resumen}}',  label: 'Resumen (texto)' },
  { group: 'Conversación', tag: '{{conversacion.mensajes}}', label: 'Mensajes (HTML)' },
  { group: 'Conversación', tag: '{{conversacion.estado}}',   label: 'Estado' },

  { group: 'Otros', tag: '{{campana.nombre}}', label: 'Nombre campaña' },
  { group: 'Otros', tag: '{{agente.nombre}}',  label: 'Nombre agente IA' },
  { group: 'Otros', tag: '{{tenant.nombre}}',  label: 'Nombre del corredor' },
  { group: 'Otros', tag: '{{fecha}}',          label: 'Fecha de hoy' },
  { group: 'Otros', tag: '{{hora}}',           label: 'Hora actual' },
]

const GROUPS = Array.from(new Set(MERGE_TAGS.map(t => t.group)))

export interface ItemsConfigShape {
  label: string
  titleColumn: string
  subtitleColumn: string
  categoryColumn: string
  amountColumn: string
  detailColumns: string[]
}

export const DEFAULT_ITEMS_CONFIG: ItemsConfigShape = {
  label: 'Pólizas',
  titleColumn: 'numero',
  subtitleColumn: 'aseguradora',
  categoryColumn: 'ramo',
  amountColumn: 'saldo',
  // Defaults expandidos: el estado de cuenta de seguros típicamente incluye
  // vigencia, último pago, prima, cuotas pendientes y aging del saldo.
  detailColumns: [
    'Vigente desde', 'Vigente hasta',
    'FechaUltimoPago', 'Forma De Pago',
    'Prima Total', 'Suma Asegurada',
    'NumeroDePagos', 'Saldo A30Dias', 'Saldo A60Dias', 'Saldo A90Dias',
    // 'Link de pago' eliminado a propósito — el link se maneja como CTA aparte,
    // no se muestra como una fila más de detalle.
  ],
}

/** Parsea el JSON de ItemsConfig persistido — siempre devuelve algo usable. */
export function parseItemsConfig(json: string | null): ItemsConfigShape {
  if (!json) return { ...DEFAULT_ITEMS_CONFIG }
  try {
    const raw = JSON.parse(json) as Partial<ItemsConfigShape>
    return {
      label:          raw.label          ?? DEFAULT_ITEMS_CONFIG.label,
      titleColumn:    raw.titleColumn    ?? DEFAULT_ITEMS_CONFIG.titleColumn,
      subtitleColumn: raw.subtitleColumn ?? DEFAULT_ITEMS_CONFIG.subtitleColumn,
      categoryColumn: raw.categoryColumn ?? DEFAULT_ITEMS_CONFIG.categoryColumn,
      amountColumn:   raw.amountColumn   ?? DEFAULT_ITEMS_CONFIG.amountColumn,
      detailColumns:  Array.isArray(raw.detailColumns) ? raw.detailColumns : DEFAULT_ITEMS_CONFIG.detailColumns,
    }
  } catch {
    return { ...DEFAULT_ITEMS_CONFIG }
  }
}

export interface EmailTemplateTabProps {
  subject: string
  htmlBody: string
  itemsConfig: ItemsConfigShape
  umbralCorporativo: number
  /** JSON crudo del archivo modelo persistido en el maestro. Null si nunca subieron archivo. */
  sampleDataJson: string | null
  onSubjectChange: (subject: string) => void
  onHtmlChange: (html: string) => void
  onItemsConfigChange: (cfg: ItemsConfigShape) => void
  onUmbralChange: (umbral: number) => void
  onSampleDataChange: (sampleDataJson: string | null) => void
}

export function EmailTemplateTab({
  subject, htmlBody, itemsConfig, umbralCorporativo, sampleDataJson,
  onSubjectChange, onHtmlChange, onItemsConfigChange, onUmbralChange, onSampleDataChange,
}: EmailTemplateTabProps) {
  const [previewModalOpen, setPreviewModalOpen] = useState(false)
  const [previewHtml, setPreviewHtml] = useState('')
  const [previewSubject, setPreviewSubject] = useState('')
  const [testEmail, setTestEmail] = useState('')
  const [testSendMessage, setTestSendMessage] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  const previewMut = usePreviewEmailTemplate()
  const testSendMut = useTestSendEmailTemplate()
  const parseSampleMut = useParseEmailSample()

  // Columnas detectadas del archivo modelo. Se llenan al subir el archivo
  // y persisten implícitamente en el sampleDataJson (al recargar el maestro
  // podemos re-derivarlas del JSON guardado).
  const detectedColumns = useMemo(() => {
    if (!sampleDataJson) return [] as string[]
    try {
      const arr = JSON.parse(sampleDataJson)
      if (Array.isArray(arr) && arr.length > 0 && typeof arr[0] === 'object') {
        return Object.keys(arr[0] as Record<string, unknown>)
      }
    } catch { /* ignore */ }
    return []
  }, [sampleDataJson])

  const fileInputRef = useRef<HTMLInputElement>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)

  /** Auto-configura ItemsConfig basándose en las columnas detectadas en el
   *  archivo modelo. Solo llena slots vacíos — respeta lo que el usuario ya
   *  configuró manualmente. Si <c>overrideDetail</c> es true, reemplaza
   *  detailColumns (útil cuando los actuales son stale).
   */
  const buildAutoConfig = (cols: string[], overrideDetail = false): ItemsConfigShape => {
    const find = (...candidates: string[]): string => {
      for (const cand of candidates) {
        const match = cols.find(c => c.toLowerCase() === cand.toLowerCase())
        if (match) return match
      }
      return ''
    }
    const smartDetail = [
      'Vigente desde', 'Vigente hasta',
      'FechaUltimoPago', 'Forma De Pago',
      'NumeroDePagos', 'Suma Asegurada',
      'Saldo A30Dias', 'Saldo A60Dias', 'Saldo A90Dias',
    ]
      .map(d => cols.find(c => c.toLowerCase() === d.toLowerCase()))
      .filter((x): x is string => !!x)

    return {
      label:          itemsConfig.label || 'Pólizas',
      titleColumn:    itemsConfig.titleColumn    || find('NroPoliza', 'numero', 'Poliza', 'KeyValue', 'NombreCliente'),
      subtitleColumn: itemsConfig.subtitleColumn || find('Aseguradora', 'aseguradora', 'InsuranceCompany'),
      categoryColumn: itemsConfig.categoryColumn || find('SubRamo', 'ramo', 'Ramo', 'Grupo Economico'),
      amountColumn:   itemsConfig.amountColumn   || find('SaldoCobrar', 'saldo', 'Saldo', 'PendingAmount', 'Monto'),
      detailColumns:  overrideDetail || itemsConfig.detailColumns.length === 0
        ? smartDetail
        : itemsConfig.detailColumns,
    }
  }

  const handleSampleUpload = async (file: File) => {
    setUploadError(null)
    try {
      const res = await parseSampleMut.mutateAsync(file)
      onSampleDataChange(res.sampleDataJson)
      onItemsConfigChange(buildAutoConfig(res.columns))
    } catch (e) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo parsear el archivo.'
      setUploadError(msg)
    }
  }

  // Auto-config retroactivo: si el maestro ya tiene sample data subido pero
  // el mapeo está vacío/incompleto/stale (caso típico: maestros existentes
  // con defaults viejos hardcoded), aplicar el mapeo inteligente automático.
  const autoConfigAppliedRef = useRef(false)
  useEffect(() => {
    if (autoConfigAppliedRef.current) return
    if (detectedColumns.length === 0) return
    // Detectar config "stale" — columnas seleccionadas que NO existen en el
    // archivo modelo. Sucede cuando el maestro tiene defaults viejos guardados
    // (ej. "marca","placa","vencimiento") pero el archivo subido tiene otras.
    const colsLower = new Set(detectedColumns.map(c => c.toLowerCase()))
    const detailStale = itemsConfig.detailColumns.length > 0
      && itemsConfig.detailColumns.every(c => !colsLower.has(c.toLowerCase()))
    const needsConfig =
      !itemsConfig.subtitleColumn ||
      !itemsConfig.titleColumn ||
      itemsConfig.detailColumns.length === 0 ||
      detailStale
    if (!needsConfig) return
    autoConfigAppliedRef.current = true
    // Si las columnas son stale, también limpiar el array para que buildAutoConfig
    // las rellene con los defaults inteligentes.
    if (detailStale) {
      onItemsConfigChange(buildAutoConfig(detectedColumns, /*overrideDetail*/ true))
    } else {
      onItemsConfigChange(buildAutoConfig(detectedColumns))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [detectedColumns])

  // Modo del editor: Visual (TipTap WYSIWYG) o HTML (textarea raw).
  // Default = Visual para usuarios no técnicos. Si el HTML actual es complejo
  // (tabla, Scriban, etc.) arrancamos en HTML para no destruirlo.
  const [editorMode, setEditorMode] = useState<EditorMode>(
    () => isComplexHtml(htmlBody) ? 'html' : 'visual'
  )

  const textareaRef = useRef<HTMLTextAreaElement>(null)

  // ── Editor TipTap (modo Visual) ──────────────────────────────────────────
  const editor = useEditor({
    extensions: [
      StarterKit,
      Link.configure({ openOnClick: false, HTMLAttributes: { rel: 'noopener noreferrer' } }),
      Image,
      Placeholder.configure({
        placeholder: 'Escribí el correo. Usá el panel de variables para personalizar.',
      }),
    ],
    content: htmlBody || '',
    onUpdate: ({ editor }) => onHtmlChange(editor.getHTML()),
  }, [editorMode]) // re-mount al cambiar de modo, así toma el htmlBody actual

  // Sincronizar el editor si el htmlBody cambia desde afuera (existing, plantilla).
  useEffect(() => {
    if (editorMode !== 'visual') return
    if (!editor) return
    if (editor.getHTML() === htmlBody) return
    editor.commands.setContent(htmlBody || '')
  }, [htmlBody, editor, editorMode])

  // ── Inserción de merge tags (funciona en ambos modos) ────────────────────
  const insertTag = (tag: string) => {
    if (editorMode === 'visual') {
      if (!editor) return
      editor.chain().focus().insertContent(tag).run()
      return
    }
    // Modo HTML — insertar en la posición del cursor del textarea
    const ta = textareaRef.current
    if (!ta) { onHtmlChange((htmlBody || '') + tag); return }
    const start = ta.selectionStart ?? htmlBody.length
    const end   = ta.selectionEnd   ?? htmlBody.length
    const next  = htmlBody.slice(0, start) + tag + htmlBody.slice(end)
    onHtmlChange(next)
    setTimeout(() => {
      const t = textareaRef.current
      if (!t) return
      t.focus()
      t.selectionStart = t.selectionEnd = start + tag.length
    }, 0)
  }

  const insertTagInSubject = (tag: string) => {
    onSubjectChange((subject || '') + tag)
  }

  // Confirmaciones — modales, no window.confirm.
  const [pendingConfirm, setPendingConfirm] = useState<{
    title: string
    description: string
    confirmLabel: string
    variant: 'danger' | 'default'
    onConfirm: () => void
  } | null>(null)

  // Switch a HTML cuando el usuario carga la plantilla sugerida (es compleja).
  const loadSuggested = () => {
    const apply = () => {
      if (!subject) onSubjectChange(SUGGESTED_EMAIL_SUBJECT)
      onHtmlChange(SUGGESTED_EMAIL_HTML)
      setEditorMode('html')
    }
    if (htmlBody) {
      setPendingConfirm({
        title: 'Cargar plantilla sugerida',
        description: 'Esto va a sobrescribir el cuerpo actual con la plantilla profesional. ¿Continuar?',
        confirmLabel: 'Sí, sobrescribir',
        variant: 'danger',
        onConfirm: apply,
      })
      return
    }
    apply()
  }

  // El modo Visual con HTML complejo muestra una vista previa renderizada en
  // iframe (no editable). NO hace falta confirmar el switch — el usuario ve el
  // correo bonito, y si quiere editar tiene el botón a HTML.
  const switchToVisual = () => setEditorMode('visual')

  const renderPayload = () => ({
    subject,
    htmlBody,
    itemsConfig: JSON.stringify(itemsConfig),
    umbralCorporativo,
    sampleDataJson: sampleDataJson ?? undefined,
  })

  const handlePreview = async () => {
    try {
      const res = await previewMut.mutateAsync(renderPayload())
      setPreviewSubject(res.subject)
      setPreviewHtml(res.htmlBody)
      setPreviewModalOpen(true)
    } catch (e) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo generar el preview.'
      setTestSendMessage({ kind: 'err', text: msg })
    }
  }

  const handleTestSend = async () => {
    if (!testEmail.trim()) {
      setTestSendMessage({ kind: 'err', text: 'Ingresá un email destinatario.' })
      return
    }
    setTestSendMessage(null)
    try {
      await testSendMut.mutateAsync({ toEmail: testEmail.trim(), ...renderPayload() })
      setTestSendMessage({ kind: 'ok', text: `Correo de prueba enviado a ${testEmail.trim()}` })
    } catch (e) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo enviar.'
      setTestSendMessage({ kind: 'err', text: msg })
    }
  }

  // Helpers para el panel de mapeo.
  const updateCfg = <K extends keyof ItemsConfigShape>(key: K, value: ItemsConfigShape[K]) =>
    onItemsConfigChange({ ...itemsConfig, [key]: value })

  const detailColumnsText = itemsConfig.detailColumns.join(', ')
  const onDetailColumnsChange = (raw: string) => {
    const cols = raw.split(',').map(s => s.trim()).filter(Boolean)
    updateCfg('detailColumns', cols)
  }

  return (
    <section className="rounded-lg bg-white p-5 shadow-sm">
      <h2 className="mb-2 text-sm font-semibold text-gray-900">Plantilla de correo</h2>
      <p className="mb-4 text-xs text-gray-500">
        Define el asunto y cuerpo del email que envía el agente cuando ejecuta una acción de correo
        (ej: <code>SEND_EMAIL_RESUME</code> al cierre de conversación). Usá variables
        como <code>{'{{cliente.nombre}}'}</code> para personalizar.
        <strong className="text-amber-700"> Si no completás el cuerpo, no se envía correo.</strong>
      </p>

      {/* ─── Archivo modelo (carga de columnas reales) ──────────────────── */}
      <div className="mb-3 rounded-md border border-blue-200 bg-blue-50 p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="flex-1 min-w-0">
            <h3 className="mb-1 text-xs font-semibold text-blue-900">Archivo modelo de la campaña</h3>
            <p className="text-[11px] text-blue-800/80 leading-relaxed">
              Subí el Excel/CSV que usás en las campañas. El sistema parsea las columnas y las
              usa para los dropdowns de mapeo abajo + previews del correo con datos reales.
              <span className="block mt-1 text-blue-700/80">Si no subís nada, el preview usa datos genéricos de muestra.</span>
            </p>
            {detectedColumns.length > 0 && (
              <div className="mt-2 flex items-center gap-1.5 text-[11px] font-medium text-emerald-700">
                <CheckCircle2 className="h-3.5 w-3.5" />
                {detectedColumns.length} columnas detectadas: {' '}
                <span className="font-mono text-emerald-700/80 truncate" title={detectedColumns.join(', ')}>
                  {detectedColumns.slice(0, 4).join(', ')}{detectedColumns.length > 4 ? '…' : ''}
                </span>
              </div>
            )}
            {uploadError && (
              <p className="mt-1 text-[11px] text-red-700"><strong>Error:</strong> {uploadError}</p>
            )}
          </div>
          <div className="shrink-0 flex flex-col gap-1">
            <input
              ref={fileInputRef}
              type="file"
              accept=".xlsx,.xls,.csv"
              className="hidden"
              onChange={(e) => {
                const f = e.target.files?.[0]
                if (f) void handleSampleUpload(f)
                e.target.value = '' // permite re-subir el mismo nombre
              }}
            />
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              disabled={parseSampleMut.isPending}
              className="inline-flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-[11px] font-semibold text-white hover:bg-blue-700 disabled:opacity-50"
            >
              <Upload className="h-3.5 w-3.5" />
              {parseSampleMut.isPending ? 'Procesando…' : (detectedColumns.length > 0 ? 'Cambiar archivo' : 'Subir archivo modelo')}
            </button>
            {detectedColumns.length > 0 && (
              <button
                type="button"
                onClick={() => { onSampleDataChange(null); setUploadError(null) }}
                className="text-[10px] text-blue-700/70 hover:text-blue-900 hover:underline"
              >
                Quitar archivo
              </button>
            )}
          </div>
        </div>
      </div>

      {/* ─── Mapeo del dataset (Fase A) ──────────────────────────────────── */}
      <div className="mb-5 rounded-md border border-gray-200 bg-gray-50 p-4">
        <h3 className="mb-1 text-xs font-semibold text-gray-800">Mapeo del dataset</h3>
        <p className="mb-3 text-[11px] text-gray-500">
          Cómo se llaman las columnas del archivo subido. El sistema agrupa por teléfono
          y arma <code>cliente.items[]</code> con N registros por cliente. Cada item expone
          título, subtítulo, categoría, monto y un grid de detalles.
          {detectedColumns.length > 0 && (
            <span className="ml-1 text-emerald-700 font-medium">
              · Los dropdowns abajo se alimentaron del archivo modelo.
            </span>
          )}
        </p>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <div>
            <label className="mb-1 block text-[10px] font-semibold uppercase tracking-wide text-gray-500">Etiqueta de la colección</label>
            <input
              type="text"
              value={itemsConfig.label}
              onChange={(e) => updateCfg('label', e.target.value)}
              placeholder="Pólizas / Productos / Préstamos"
              className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-xs focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </div>
          <ColumnSelector
            label="Columna del título"
            value={itemsConfig.titleColumn}
            placeholder="numero"
            options={detectedColumns}
            onChange={(v) => updateCfg('titleColumn', v)}
          />
          <ColumnSelector
            label="Columna del subtítulo"
            value={itemsConfig.subtitleColumn}
            placeholder="aseguradora"
            options={detectedColumns}
            onChange={(v) => updateCfg('subtitleColumn', v)}
          />
          <ColumnSelector
            label="Columna de categoría (badge)"
            value={itemsConfig.categoryColumn}
            placeholder="ramo"
            options={detectedColumns}
            onChange={(v) => updateCfg('categoryColumn', v)}
          />
          <ColumnSelector
            label="Columna del monto"
            value={itemsConfig.amountColumn}
            placeholder="saldo"
            options={detectedColumns}
            onChange={(v) => updateCfg('amountColumn', v)}
          />
          <div>
            <label className="mb-1 block text-[10px] font-semibold uppercase tracking-wide text-gray-500">Umbral cliente corporativo</label>
            <div className="flex items-center gap-2">
              <input
                type="number"
                min={1}
                max={9999}
                value={umbralCorporativo}
                onChange={(e) => onUmbralChange(parseInt(e.target.value, 10) || 10)}
                className="w-20 rounded-md border border-gray-300 px-2.5 py-1.5 text-xs focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
              <span className="text-[11px] text-gray-500">items o más → layout corporativo</span>
            </div>
          </div>
        </div>
        <div className="mt-3">
          <label className="mb-1 block text-[10px] font-semibold uppercase tracking-wide text-gray-500">
            Columnas a mostrar en el detalle
          </label>
          {detectedColumns.length > 0 ? (
            <div className="flex flex-wrap gap-1.5 rounded-md border border-gray-200 bg-white p-2">
              {detectedColumns.map((col) => {
                const selected = itemsConfig.detailColumns.includes(col)
                return (
                  <button
                    type="button"
                    key={col}
                    onClick={() => {
                      const next = selected
                        ? itemsConfig.detailColumns.filter(c => c !== col)
                        : [...itemsConfig.detailColumns, col]
                      updateCfg('detailColumns', next)
                    }}
                    className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[11px] font-medium transition-colors ${
                      selected
                        ? 'bg-blue-600 text-white'
                        : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                    }`}
                  >
                    {selected && <CheckCircle2 className="h-3 w-3" />}
                    <span className="font-mono">{col}</span>
                  </button>
                )
              })}
            </div>
          ) : (
            <input
              type="text"
              value={detailColumnsText}
              onChange={(e) => onDetailColumnsChange(e.target.value)}
              placeholder="marca, placa, vencimiento, cuotas_pendientes"
              className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-xs font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          )}
          <p className="mt-1 text-[10px] text-gray-400">
            {detectedColumns.length > 0
              ? 'Click para activar/desactivar cada columna que querés ver en el detalle del email.'
              : 'Sólo aparecen las columnas que existan en el archivo subido. Si una columna no está, se omite.'}
          </p>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-[1fr_240px]">
        <div className="space-y-4">
          {/* Asunto */}
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-700">Asunto</label>
            <input
              type="text"
              value={subject}
              onChange={(e) => onSubjectChange(e.target.value)}
              placeholder="Recordatorio de pago - {{cliente.nombre}}"
              className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </div>

          {/* Editor dual: Visual (fácil) | HTML (técnico) */}
          <div>
            <div className="mb-1 flex flex-wrap items-center justify-between gap-2">
              <label className="block text-xs font-medium text-gray-700">Cuerpo</label>
              <div className="flex items-center gap-2">
                {/* Toggle Visual / HTML */}
                <div className="inline-flex overflow-hidden rounded-md border border-gray-300 bg-white text-[11px]">
                  <button
                    type="button"
                    onClick={switchToVisual}
                    className={`inline-flex items-center gap-1 px-2.5 py-1 font-medium transition-colors ${
                      editorMode === 'visual'
                        ? 'bg-blue-600 text-white'
                        : 'text-gray-600 hover:bg-gray-50'
                    }`}
                    title="Editor visual — fácil para usuarios no técnicos"
                  >
                    <Pencil className="h-3 w-3" /> Visual
                  </button>
                  <button
                    type="button"
                    onClick={() => setEditorMode('html')}
                    className={`inline-flex items-center gap-1 border-l border-gray-300 px-2.5 py-1 font-medium transition-colors ${
                      editorMode === 'html'
                        ? 'bg-blue-600 text-white'
                        : 'text-gray-600 hover:bg-gray-50'
                    }`}
                    title="Editor HTML — para usuarios técnicos / plantillas con tablas o Scriban"
                  >
                    <Code className="h-3 w-3" /> HTML
                  </button>
                </div>
                <button
                  type="button"
                  onClick={loadSuggested}
                  className="inline-flex items-center gap-1.5 rounded-md border border-amber-200 bg-amber-50 px-2.5 py-1 text-[11px] font-medium text-amber-700 hover:bg-amber-100"
                  title="Carga un HTML profesional listo para editar — abre en modo HTML."
                >
                  <Sparkles className="h-3 w-3" /> Cargar plantilla sugerida
                </button>
              </div>
            </div>

            {/* Modo Visual — depende de la complejidad del HTML.
                Si es simple → TipTap WYSIWYG editable (paragraphs, listas, bold).
                Si es complejo → iframe que renderiza el correo como se vería real,
                con banner explicativo para editar via HTML. */}
            {editorMode === 'visual' && isComplexHtml(htmlBody) && (
              <ComplexHtmlPreview
                htmlBody={htmlBody}
                onSwitchToHtml={() => setEditorMode('html')}
              />
            )}
            {editorMode === 'visual' && !isComplexHtml(htmlBody) && (
              <div className="overflow-hidden rounded-md border border-gray-300">
                <EditorToolbar editor={editor} />
                <EditorContent
                  editor={editor}
                  className="prose prose-sm min-h-[300px] max-w-none p-3 focus:outline-none [&_.ProseMirror]:min-h-[280px] [&_.ProseMirror]:outline-none"
                />
              </div>
            )}

            {/* Modo HTML — textarea raw */}
            {editorMode === 'html' && (
              <textarea
                ref={textareaRef}
                value={htmlBody}
                onChange={(e) => onHtmlChange(e.target.value)}
                placeholder={'<table>\n  <tr><td>Hola {{cliente.nombre}}, …</td></tr>\n</table>'}
                spellCheck={false}
                rows={20}
                className="w-full rounded-md border border-gray-300 px-3 py-2 font-mono text-[11.5px] leading-relaxed focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                style={{ minHeight: 360, whiteSpace: 'pre', overflow: 'auto' }}
              />
            )}

            <p className="mt-1 text-[11px] text-gray-400">
              {editorMode === 'visual'
                ? <>Modo Visual — escribí texto, formateá con la barra. Las variables son texto literal: se reemplazan al enviar.</>
                : <>HTML literal — preserva tablas, estilos inline y directivas Scriban (<code>{'{{ if }}'}</code>, <code>{'{{ for }}'}</code>).</>}
              <span className="ml-2 text-gray-300">{htmlBody.length.toLocaleString()} caracteres</span>
            </p>
          </div>

          {/* Preview + test send */}
          <div className="flex flex-wrap items-center gap-2 border-t border-gray-100 pt-3">
            <button
              type="button"
              onClick={handlePreview}
              disabled={previewMut.isPending || !htmlBody}
              className="inline-flex items-center gap-1.5 rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            >
              <Eye className="h-3.5 w-3.5" />
              {previewMut.isPending ? 'Generando…' : 'Vista previa'}
            </button>
            <div className="flex flex-1 items-center gap-2">
              <input
                type="email"
                value={testEmail}
                onChange={(e) => setTestEmail(e.target.value)}
                placeholder="email@destinatario.com"
                className="min-w-[200px] flex-1 rounded-md border border-gray-300 px-3 py-1.5 text-xs focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
              <button
                type="button"
                onClick={handleTestSend}
                disabled={testSendMut.isPending || !htmlBody || !testEmail.trim()}
                className="inline-flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50"
              >
                <Send className="h-3.5 w-3.5" />
                {testSendMut.isPending ? 'Enviando…' : 'Enviar prueba'}
              </button>
            </div>
          </div>
          {testSendMessage && (
            <p className={`text-xs ${testSendMessage.kind === 'ok' ? 'text-emerald-700' : 'text-red-600'}`}>
              {testSendMessage.text}
            </p>
          )}
        </div>

        {/* Panel lateral — variables disponibles */}
        <aside className="rounded-md border border-gray-200 bg-gray-50 p-3">
          <h3 className="mb-2 text-xs font-semibold text-gray-700">Variables</h3>
          <p className="mb-3 text-[11px] text-gray-500">
            Click para insertar en el cuerpo. Para el asunto, podés escribirlas o usar el ícono 📄.
          </p>
          {GROUPS.map((group) => (
            <div key={group} className="mb-3 last:mb-0">
              <div className="mb-1 text-[10px] font-semibold uppercase tracking-wide text-gray-400">{group}</div>
              <div className="flex flex-wrap gap-1">
                {MERGE_TAGS.filter(t => t.group === group).map(({ tag, label }) => (
                  <div key={tag} className="inline-flex items-stretch overflow-hidden rounded-md border border-gray-200 bg-white text-[11px] hover:border-blue-300">
                    <button
                      type="button"
                      onClick={() => insertTag(tag)}
                      title={`Insertar ${tag} en el cuerpo`}
                      className="px-1.5 py-1 text-gray-700 hover:bg-blue-50 hover:text-blue-700"
                    >
                      {label}
                    </button>
                    <button
                      type="button"
                      onClick={() => insertTagInSubject(tag)}
                      title="Insertar al final del asunto"
                      className="border-l border-gray-200 px-1.5 py-1 text-gray-400 hover:bg-gray-50 hover:text-gray-700"
                    >
                      <CopyIcon className="h-3 w-3" />
                    </button>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </aside>
      </div>

      {/* Modal preview */}
      {previewModalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
          onClick={() => setPreviewModalOpen(false)}
        >
          <div
            className="max-h-[90vh] w-full max-w-3xl overflow-hidden rounded-lg bg-white shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <header className="flex items-start justify-between border-b border-gray-200 px-5 py-3">
              <div>
                <p className="text-xs text-gray-500">Vista previa con datos de ejemplo</p>
                <h3 className="text-base font-semibold text-gray-900">{previewSubject || '(sin asunto)'}</h3>
              </div>
              <button onClick={() => setPreviewModalOpen(false)} className="rounded-md p-1 text-gray-500 hover:bg-gray-100">
                <X className="h-4 w-4" />
              </button>
            </header>
            <div className="max-h-[calc(90vh-4rem)] overflow-auto bg-gray-50 p-6">
              <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
                <div className="prose prose-sm max-w-none" dangerouslySetInnerHTML={{ __html: previewHtml }} />
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Modal lujosa para confirmaciones (reemplaza window.confirm) */}
      <ConfirmDialog
        open={!!pendingConfirm}
        onClose={() => setPendingConfirm(null)}
        onConfirm={() => {
          const c = pendingConfirm
          setPendingConfirm(null)
          c?.onConfirm()
        }}
        title={pendingConfirm?.title ?? ''}
        description={pendingConfirm?.description ?? ''}
        confirmLabel={pendingConfirm?.confirmLabel ?? 'Confirmar'}
        variant={pendingConfirm?.variant ?? 'default'}
      />
    </section>
  )
}

/** Vista previa renderizada en iframe del HTML complejo (con tablas / Scriban /
 *  inline styles). El iframe aísla los estilos del email del CSS de la app y
 *  muestra el correo tal cual se vería en un cliente. Read-only — para editar
 *  el usuario va al modo HTML. */
function ComplexHtmlPreview({
  htmlBody, onSwitchToHtml,
}: { htmlBody: string; onSwitchToHtml: () => void }) {
  // Envolvemos el HTML en un documento mínimo para que el iframe lo renderice
  // bien (sin scrollbars internos extras ni márgenes del body).
  const docHtml = `<!doctype html><html><head><meta charset="utf-8"><style>
    body{margin:0;padding:16px;background:#f4f5f7;font-family:-apple-system,Segoe UI,Roboto,Inter,Arial,sans-serif}
    *{box-sizing:border-box}
  </style></head><body>${htmlBody}</body></html>`

  return (
    <div className="overflow-hidden rounded-md border border-gray-300">
      {/* Banner sobre el iframe */}
      <div className="flex items-center justify-between gap-2 border-b border-amber-200 bg-amber-50 px-3 py-2 text-[11px] text-amber-800">
        <span className="flex items-center gap-1.5">
          <Eye className="h-3.5 w-3.5" />
          <span>Vista previa del correo. Esta plantilla usa HTML avanzado y se ve mejor en su forma original.</span>
        </span>
        <button
          type="button"
          onClick={onSwitchToHtml}
          className="inline-flex items-center gap-1 rounded border border-amber-300 bg-white px-2 py-0.5 font-medium text-amber-800 hover:bg-amber-100"
        >
          <Code className="h-3 w-3" /> Editar HTML
        </button>
      </div>
      <iframe
        title="Vista previa del correo"
        srcDoc={docHtml}
        sandbox=""
        className="block w-full bg-white"
        style={{ height: 480, border: 0 }}
      />
    </div>
  )
}

/** Selector de columna para el mapeo del email. Si hay columnas detectadas
 *  (archivo modelo subido) muestra dropdown; si no, input libre. */
function ColumnSelector({
  label, value, placeholder, options, onChange,
}: {
  label: string
  value: string
  placeholder: string
  options: string[]
  onChange: (v: string) => void
}) {
  return (
    <div>
      <label className="mb-1 block text-[10px] font-semibold uppercase tracking-wide text-gray-500">{label}</label>
      {options.length > 0 ? (
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className="w-full rounded-md border border-gray-300 bg-white px-2.5 py-1.5 text-xs font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        >
          <option value="">(vacío)</option>
          {options.map(c => (
            <option key={c} value={c}>{c}</option>
          ))}
        </select>
      ) : (
        <input
          type="text"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          className="w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-xs font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
      )}
    </div>
  )
}

/** Toolbar simple para el modo Visual del editor de email. Solo formatos
 *  básicos que se traducen a HTML semántico (no rompen el render del correo). */
function EditorToolbar({ editor }: { editor: Editor | null }) {
  if (!editor) return null
  const btn = (
    active: boolean,
    onClick: () => void,
    Icon: React.ComponentType<{ className?: string }>,
    title: string,
  ) => (
    <button
      type="button"
      onClick={onClick}
      title={title}
      className={`rounded p-1 ${active ? 'bg-blue-100 text-blue-700' : 'text-gray-600 hover:bg-gray-100'}`}
    >
      <Icon className="h-3.5 w-3.5" />
    </button>
  )
  return (
    <div className="flex flex-wrap items-center gap-0.5 border-b border-gray-200 bg-gray-50 px-2 py-1">
      {btn(editor.isActive('heading', { level: 1 }), () => editor.chain().focus().toggleHeading({ level: 1 }).run(), Heading1, 'Título grande')}
      {btn(editor.isActive('heading', { level: 2 }), () => editor.chain().focus().toggleHeading({ level: 2 }).run(), Heading2, 'Título mediano')}
      {btn(editor.isActive('heading', { level: 3 }), () => editor.chain().focus().toggleHeading({ level: 3 }).run(), Heading3, 'Título chico')}
      <span className="mx-1 h-4 w-px bg-gray-300" />
      {btn(editor.isActive('bold'),   () => editor.chain().focus().toggleBold().run(),   Bold,          'Negrita')}
      {btn(editor.isActive('italic'), () => editor.chain().focus().toggleItalic().run(), Italic,        'Itálica')}
      {btn(editor.isActive('strike'), () => editor.chain().focus().toggleStrike().run(), UnderlineIcon, 'Tachado')}
      <span className="mx-1 h-4 w-px bg-gray-300" />
      {btn(editor.isActive('bulletList'),  () => editor.chain().focus().toggleBulletList().run(),  List,        'Lista')}
      {btn(editor.isActive('orderedList'), () => editor.chain().focus().toggleOrderedList().run(), ListOrdered, 'Lista numerada')}
      {btn(editor.isActive('blockquote'),  () => editor.chain().focus().toggleBlockquote().run(),  Quote,       'Cita')}
      <span className="mx-1 h-4 w-px bg-gray-300" />
      {btn(editor.isActive('link'), async () => {
        const prev = editor.getAttributes('link').href as string | undefined
        const url = await promptDialog({ title: 'URL del link', defaultValue: prev ?? 'https://', inputType: 'url' })
        if (url === null) return
        if (url === '') editor.chain().focus().extendMarkRange('link').unsetLink().run()
        else            editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
      }, LinkIcon, 'Link')}
      {btn(false, async () => {
        const url = await promptDialog({ title: 'URL de la imagen', defaultValue: 'https://', inputType: 'url' })
        if (url) editor.chain().focus().setImage({ src: url }).run()
      }, ImageIcon, 'Imagen')}
    </div>
  )
}

