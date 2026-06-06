import { useMemo, useRef, useState, type ChangeEvent, type DragEvent } from 'react'
import {
  Plus, RefreshCw, Pencil, Trash2, X, CheckCircle2, Clock, XCircle, FileText, Send, Save, HelpCircle, Sparkles,
  UploadCloud, FileSpreadsheet, Loader2, Variable, Eye, Power,
} from 'lucide-react'
import {
  useMetaTemplates, useCreateMetaTemplate, useUpdateMetaTemplate,
  useToggleMetaTemplate, useSyncMetaTemplate, useSyncAllMetaTemplates, useGenerateFromPrompt,
  useSubmitMetaTemplate, useDeleteMetaTemplate, useAvailableFields, useUpdateMetaTemplateMapping,
  type MetaTemplatePayload,
} from '@/shared/hooks/useMetaTemplates'
import { useParseEmailSample } from '@/shared/hooks/useCampaignTemplates'
import type { MetaMessageTemplate, MetaTemplateCategory, MetaTemplateStatus, MetaTemplatePurpose } from '@/shared/types/models'
import { confirmDialog, toast } from '@/shared/components/dialog'

/** Cuenta los placeholders distintos {{n}} en un texto y devuelve el máximo n. */
function countVars(text: string | null | undefined): number {
  if (!text) return 0
  const set = new Set<number>()
  for (const m of text.matchAll(/\{\{\s*(\d+)\s*\}\}/g)) {
    const n = parseInt(m[1])
    if (!Number.isNaN(n)) set.add(n)
  }
  return set.size
}

const STATUS_META: Record<MetaTemplateStatus, { label: string; cls: string; Icon: typeof CheckCircle2 }> = {
  APPROVED: { label: 'Aprobada', cls: 'bg-green-100 text-green-800 border-green-200', Icon: CheckCircle2 },
  PENDING: { label: 'En revisión', cls: 'bg-amber-100 text-amber-800 border-amber-200', Icon: Clock },
  REJECTED: { label: 'Rechazada', cls: 'bg-red-100 text-red-800 border-red-200', Icon: XCircle },
  DRAFT: { label: 'Borrador', cls: 'bg-gray-100 text-gray-700 border-gray-200', Icon: FileText },
  PAUSED: { label: 'Pausada', cls: 'bg-orange-100 text-orange-800 border-orange-200', Icon: Clock },
  DISABLED: { label: 'Deshabilitada', cls: 'bg-gray-100 text-gray-700 border-gray-200', Icon: XCircle },
}

const CATEGORIES: { value: MetaTemplateCategory; label: string; hint: string; examples: string; recommended?: boolean }[] = [
  {
    value: 'UTILITY', label: 'Utilidad', recommended: true,
    hint: 'Mensajes transaccionales ligados a una gestión o acción del cliente. Aprobación más rápida y costo menor.',
    examples: 'Recordatorio de pago, estado de cuenta, vencimiento de póliza, confirmaciones.',
  },
  {
    value: 'MARKETING', label: 'Marketing',
    hint: 'Mensajes promocionales o no transaccionales. Revisión más estricta, mayor costo y con límites de frecuencia por usuario.',
    examples: 'Ofertas, novedades, campañas de re-contacto, invitaciones.',
  },
  {
    value: 'AUTHENTICATION', label: 'Autenticación',
    hint: 'Códigos de un solo uso (OTP) para verificar identidad o login. Formato especial con el código y botón de copiar.',
    examples: 'Código de verificación 2FA.',
  },
]

interface FormState {
  id?: string
  name: string
  language: string
  category: MetaTemplateCategory
  purpose: MetaTemplatePurpose
  headerText: string
  bodyText: string
  footerText: string
  headerSamples: string[]
  bodySamples: string[]
  bodyMapping: string[]   // campo para cada {{n}} del cuerpo
}

const EMPTY_FORM: FormState = {
  name: '', language: 'es', category: 'UTILITY', purpose: 'Launch',
  headerText: '', bodyText: '', footerText: '',
  headerSamples: [], bodySamples: [], bodyMapping: [],
}

export function MetaTemplatesTab({ lineId, campaignTemplateId, baseName }: {
  lineId: string
  campaignTemplateId?: string
  baseName?: string
}) {
  const { data: templates, isLoading } = useMetaTemplates(lineId)
  const createMut = useCreateMetaTemplate(lineId, campaignTemplateId)
  const updateMut = useUpdateMetaTemplate(lineId)
  const toggleMut = useToggleMetaTemplate(lineId)
  const syncMut = useSyncMetaTemplate(lineId)
  const syncAllMut = useSyncAllMetaTemplates(lineId)
  const generateMut = useGenerateFromPrompt(lineId)
  const submitMut = useSubmitMetaTemplate(lineId)
  const deleteMut = useDeleteMetaTemplate(lineId)
  const mappingMut = useUpdateMetaTemplateMapping(lineId)
  const parseSampleMut = useParseEmailSample()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [showExcelModal, setShowExcelModal] = useState(false)
  const [isDragging, setIsDragging] = useState(false)
  const processing = parseSampleMut.isPending || generateMut.isPending

  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState<FormState>(EMPTY_FORM)
  const [showCatHelp, setShowCatHelp] = useState(false)
  const isEditing = !!form.id

  // Plantilla a previsualizar en la modal "ojo" (ver contenido completo).
  const [viewTemplate, setViewTemplate] = useState<MetaMessageTemplate | null>(null)

  // Editor de mapeo {{n}}→campo (funciona incluso en plantillas aprobadas).
  // Flujo: (1) subir el Excel de la campaña → (2) mapear con sus columnas reales.
  const [mappingTarget, setMappingTarget] = useState<MetaMessageTemplate | null>(null)
  const [mappingDraft, setMappingDraft] = useState<string[]>([])
  const [mappingFields, setMappingFields] = useState<string[] | null>(null)  // null = falta subir Excel
  const [mappingDragging, setMappingDragging] = useState(false)
  const mappingFileRef = useRef<HTMLInputElement>(null)
  const mappingVarCount = mappingTarget ? countVars(mappingTarget.bodyText) : 0

  function openMapping(t: MetaMessageTemplate) {
    const n = countVars(t.bodyText)
    const draft = Array.from({ length: n }, (_, i) => t.bodyMapping?.[i] ?? '')
    setMappingDraft(draft)
    setMappingFields(null)   // siempre pedir el Excel primero (columnas reales)
    setMappingTarget(t)
  }

  async function onMappingFile(file: File) {
    try {
      const parsed = await parseSampleMut.mutateAsync(file)
      if (!parsed.columns || parsed.columns.length === 0) {
        toast.error('No se detectaron columnas en el archivo. Revisá que tenga encabezados.'); return
      }
      setMappingFields(parsed.columns)
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo leer el archivo.'
      toast.error(msg)
    }
  }

  async function saveMapping() {
    if (!mappingTarget) return
    if (mappingDraft.slice(0, mappingVarCount).some(v => !v)) {
      toast.error('Asigná un campo a cada variable {{n}}.'); return
    }
    try {
      await mappingMut.mutateAsync({ id: mappingTarget.id, bodyMapping: mappingDraft.slice(0, mappingVarCount) })
      toast.success('Mapeo guardado. Ya podés probar la plantilla.')
      setMappingTarget(null)
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo guardar el mapeo.'
      toast.error(msg)
    }
  }

  // Campos del maestro para mapear {{n}}→campo (estándar + columnas del proceso/Excel).
  const { data: availableFields } = useAvailableFields(campaignTemplateId)

  const headerVars = useMemo(() => Math.min(countVars(form.headerText), 1), [form.headerText])
  const bodyVars = useMemo(() => countVars(form.bodyText), [form.bodyText])

  function resetForm() { setForm(EMPTY_FORM); setShowForm(false) }

  function startCreate() { setForm(EMPTY_FORM); setShowForm(true) }

  function startEdit(t: MetaMessageTemplate) {
    setForm({
      id: t.id,
      name: t.name,
      language: t.language,
      category: t.category,
      purpose: t.purpose ?? 'Launch',
      headerText: t.headerText ?? '',
      bodyText: t.bodyText,
      footerText: t.footerText ?? '',
      headerSamples: t.headerSamples ?? [],
      bodySamples: t.bodySamples ?? [],
      bodyMapping: t.bodyMapping ?? [],
    })
    setShowForm(true)
  }

  function setSample(kind: 'header' | 'body', idx: number, value: string) {
    setForm(f => {
      const arr = [...(kind === 'header' ? f.headerSamples : f.bodySamples)]
      arr[idx] = value
      return kind === 'header' ? { ...f, headerSamples: arr } : { ...f, bodySamples: arr }
    })
  }

  function setMapping(idx: number, value: string) {
    setForm(f => {
      const arr = [...f.bodyMapping]
      arr[idx] = value
      return { ...f, bodyMapping: arr }
    })
  }

  async function submit(submitToMeta: boolean) {
    // Validación cliente — alinea con el backend para evitar rebotes.
    if (!form.name.trim()) { toast.error('El nombre es obligatorio.'); return }
    if (!form.bodyText.trim()) { toast.error('El cuerpo es obligatorio.'); return }
    const hSamples = form.headerSamples.slice(0, headerVars).filter(s => s.trim())
    const bSamples = form.bodySamples.slice(0, bodyVars).filter(s => s.trim())
    if (hSamples.length !== headerVars) { toast.error('Completá el ejemplo del encabezado.'); return }
    if (bSamples.length !== bodyVars) { toast.error(`Completá los ${bodyVars} ejemplo(s) del cuerpo.`); return }
    const bMapping = form.bodyMapping.slice(0, bodyVars).map(s => (s ?? '').trim())
    if (bMapping.some(f2 => !f2) || bMapping.length !== bodyVars) {
      toast.error(`Asigná el campo de cada variable {{n}} (mapeo). Faltan ${bodyVars - bMapping.filter(Boolean).length}.`); return
    }

    const payload: MetaTemplatePayload = {
      name: form.name.trim(),
      language: form.language.trim() || 'es',
      category: form.category,
      purpose: form.purpose,
      headerText: form.headerText.trim() || null,
      bodyText: form.bodyText.trim(),
      footerText: form.footerText.trim() || null,
      headerSamples: hSamples,
      bodySamples: bSamples,
      bodyMapping: bMapping,
      submitToMeta,
    }

    try {
      if (isEditing) await updateMut.mutateAsync({ id: form.id!, ...payload })
      else await createMut.mutateAsync(payload)
      toast.success(submitToMeta ? 'Plantilla enviada a Meta para revisión.' : 'Borrador guardado.')
      resetForm()
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo guardar la plantilla.'
      toast.error(msg)
    }
  }

  async function onDelete(t: MetaMessageTemplate) {
    const ok = await confirmDialog({
      title: 'Eliminar plantilla',
      description: `¿Eliminar la plantilla "${t.name}"? Si ya fue enviada a Meta, también se borrará allí.`,
      variant: 'danger',
    })
    if (!ok) return
    try { await deleteMut.mutateAsync(t.id); toast.success('Plantilla eliminada.') }
    catch (e: unknown) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo eliminar.'
      toast.error(msg)
    }
  }

  async function onSync(t: MetaMessageTemplate) {
    try { await syncMut.mutateAsync(t.id); toast.success('Estado sincronizado con Meta.') }
    catch { toast.error('No se pudo sincronizar.') }
  }

  async function onSubmit(t: MetaMessageTemplate) {
    const ok = await confirmDialog({
      title: 'Enviar a Meta para revisión',
      description: `Se enviará "${t.name}" a Meta para su aprobación. Una vez en revisión no se puede editar (tendrías que crear otra versión). ¿Continuar?`,
    })
    if (!ok) return
    try {
      await submitMut.mutateAsync(t.id)
      toast.success('Plantilla enviada a Meta. Quedó "En revisión".')
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo enviar a Meta.'
      toast.error(msg)
    }
  }

  async function onSyncAll() {
    try {
      const res = await syncAllMut.mutateAsync()
      toast.success(`Sincronizado con Meta: ${res.imported} importada(s), ${res.updated} actualizada(s).`)
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo sincronizar con Meta.'
      toast.error(msg)
    }
  }

  async function onGenerate() {
    if (!campaignTemplateId) return
    try {
      const res = await generateMut.mutateAsync({ campaignTemplateId, baseName })
      if (res.needsStructure) {
        // Sin proceso de descarga ni datos de ejemplo: abrimos el modal para pedir el Excel.
        setShowExcelModal(true)
        return
      }
      setShowForm(false) // cerrar el form para que la lista muestre los borradores nuevos
      toast.success(`Se generaron ${res.count} borrador(es) desde el prompt. Revisalos y enviá a Meta.`)
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo generar desde el prompt.'
      toast.error(msg)
    }
  }

  // Parsea el Excel elegido (modal) y reintenta la generación con sus columnas reales.
  async function processExcel(file: File) {
    if (!campaignTemplateId) return
    try {
      const parsed = await parseSampleMut.mutateAsync(file)
      if (!parsed.columns || parsed.columns.length === 0) {
        toast.error('No se detectaron columnas en el archivo. Revisá que tenga encabezados.')
        return
      }
      const res = await generateMut.mutateAsync({
        campaignTemplateId, baseName,
        columns: parsed.columns, sampleDataJson: parsed.sampleDataJson ?? undefined,
      })
      setShowExcelModal(false)
      setShowForm(false)
      toast.success(`Se generaron ${res.count} borrador(es) usando ${parsed.columns.length} columnas del Excel.`)
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'No se pudo procesar el Excel.'
      toast.error(msg)
    }
  }

  function onExcelSelected(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = '' // permitir re-seleccionar el mismo archivo
    if (file) processExcel(file)
  }

  function onExcelDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault()
    setIsDragging(false)
    const file = e.dataTransfer.files?.[0]
    if (file) processExcel(file)
  }

  return (
    <div className="space-y-4">
      {/* Input oculto: lo dispara el botón del modal. */}
      <input ref={fileInputRef} type="file" accept=".xlsx,.xls,.csv" className="hidden" onChange={onExcelSelected} />

      {/* ── Modal: mapeo {{n}}→campo (funciona incluso en aprobadas) ── */}
      {mappingTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 backdrop-blur-sm p-4"
          onClick={() => !mappingMut.isPending && setMappingTarget(null)}>
          <div className="flex max-h-[90vh] w-full max-w-lg flex-col overflow-hidden rounded-2xl bg-white shadow-2xl" onClick={ev => ev.stopPropagation()}>
            <div className="shrink-0 bg-gradient-to-br from-[#1a3a6b] via-[#1e457e] to-[#2d5a9e] px-6 py-4 text-white">
              <div className="flex items-center gap-3">
                <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-white/20"><Variable size={22} /></div>
                <div>
                  <h3 className="text-base font-bold leading-tight">Mapeo de variables</h3>
                  <p className="text-xs text-white/85 font-mono">{mappingTarget.name}</p>
                </div>
              </div>
            </div>
            <input ref={mappingFileRef} type="file" accept=".xlsx,.xls,.csv" className="hidden"
              onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; if (f) onMappingFile(f) }} />

            {mappingFields === null ? (
              /* ── Paso 1: subir el Excel de la campaña ── */
              <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4 space-y-3">
                <p className="text-sm text-gray-600">
                  Subí el <b>Excel (o CSV)</b> que vas a usar en la campaña. Leeremos sus
                  <b> columnas reales</b> para mapear cada variable de la plantilla.
                </p>
                <div
                  onClick={() => !parseSampleMut.isPending && mappingFileRef.current?.click()}
                  onDragOver={e => { e.preventDefault(); setMappingDragging(true) }}
                  onDragLeave={() => setMappingDragging(false)}
                  onDrop={e => { e.preventDefault(); setMappingDragging(false); const f = e.dataTransfer.files?.[0]; if (f) onMappingFile(f) }}
                  className={`flex cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors ${
                    mappingDragging ? 'border-[#1a3a6b] bg-blue-50' : 'border-gray-300 hover:border-[#2d5a9e] hover:bg-gray-50'
                  } ${parseSampleMut.isPending ? 'pointer-events-none opacity-60' : ''}`}>
                  {parseSampleMut.isPending ? (
                    <><Loader2 size={32} className="animate-spin text-[#1a3a6b]" /><p className="text-sm font-medium text-gray-700">Leyendo columnas…</p></>
                  ) : (
                    <>
                      <div className="flex h-14 w-14 items-center justify-center rounded-full bg-blue-100"><UploadCloud size={28} className="text-[#1a3a6b]" /></div>
                      <p className="text-sm font-semibold text-gray-800">Arrastrá tu archivo aquí</p>
                      <p className="text-xs text-gray-500">o <span className="font-medium text-[#1a3a6b]">hacé clic para elegir</span></p>
                      <p className="mt-1 text-[11px] text-gray-400">Formatos: .xlsx · .xls · .csv</p>
                    </>
                  )}
                </div>
                <p className="text-[11px] text-gray-400">💡 Solo se leen los encabezados. No se guarda el archivo.</p>
              </div>
            ) : (
              /* ── Paso 2: mapear cada {{n}} a las columnas reales del archivo ── */
              <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4 space-y-3">
                <div className="flex items-center justify-between">
                  <p className="text-xs text-gray-500">
                    Asigná cada <span className="font-mono">{'{{n}}'}</span> a una <b>columna del archivo</b>.
                  </p>
                  <button type="button" onClick={() => setMappingFields(null)} className="text-[11px] font-medium text-[#1a3a6b] hover:underline">Cambiar archivo</button>
                </div>
                <p className="max-h-24 overflow-y-auto rounded-lg bg-gray-50 border border-gray-200 p-2 text-sm text-gray-700 whitespace-pre-wrap">{mappingTarget.bodyText}</p>
                <div className="space-y-2">
                  {Array.from({ length: mappingVarCount }).map((_, i) => (
                    <div key={i} className="flex items-center gap-2">
                      <span className="w-10 shrink-0 font-mono text-xs font-semibold text-indigo-600">{`{{${i + 1}}}`}</span>
                      <select value={mappingDraft[i] ?? ''}
                        onChange={e => setMappingDraft(d => { const n = [...d]; n[i] = e.target.value; return n })}
                        className="flex-1 rounded-lg border border-gray-300 px-2 py-1.5 text-sm">
                        <option value="">— columna —</option>
                        {mappingFields.map(fld => <option key={fld} value={fld}>{fld}</option>)}
                      </select>
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div className="flex shrink-0 justify-end gap-2 border-t border-gray-100 px-6 py-4">
              <button type="button" onClick={() => setMappingTarget(null)} disabled={mappingMut.isPending}
                className="rounded-lg border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50">Cancelar</button>
              <button type="button" onClick={saveMapping} disabled={mappingMut.isPending || mappingFields === null}
                className="inline-flex items-center gap-1.5 rounded-lg bg-[#1a3a6b] px-4 py-2 text-sm font-semibold text-white hover:bg-[#234a85] disabled:opacity-50">
                <Save size={15} /> Guardar mapeo
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Modal: ver plantilla completa (ícono ojo del grid) ── */}
      {viewTemplate && (() => {
        const sm = STATUS_META[viewTemplate.metaStatus] ?? STATUS_META.DRAFT
        return (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 backdrop-blur-sm p-4"
            onClick={() => setViewTemplate(null)}>
            <div className="flex max-h-[90vh] w-full max-w-lg flex-col overflow-hidden rounded-2xl bg-white shadow-2xl" onClick={ev => ev.stopPropagation()}>
              <div className="shrink-0 bg-gradient-to-br from-[#1a3a6b] via-[#1e457e] to-[#2d5a9e] px-6 py-4 text-white">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h3 className="font-mono text-base font-bold leading-tight break-all">{viewTemplate.name}</h3>
                    <p className="text-xs text-white/85">
                      {viewTemplate.language} · {viewTemplate.category} · {viewTemplate.purpose === 'FollowUp' ? 'Seguimiento' : 'Lanzamiento'}
                    </p>
                  </div>
                  <button type="button" onClick={() => setViewTemplate(null)} className="rounded-lg p-1 text-white/80 hover:bg-white/20 hover:text-white"><X size={18} /></button>
                </div>
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4 space-y-3">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium ${sm.cls}`}><sm.Icon size={12} /> {sm.label}</span>
                  <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${viewTemplate.isEnabled ? 'bg-blue-50 text-blue-700' : 'bg-gray-100 text-gray-500'}`}>{viewTemplate.isEnabled ? 'Activa' : 'Inactiva'}</span>
                </div>
                {/* Vista tipo burbuja de WhatsApp */}
                <div className="rounded-xl border border-[#cdebbf] bg-[#e7ffdb] p-3 space-y-2">
                  {viewTemplate.headerText && <p className="text-sm font-semibold text-gray-800 whitespace-pre-wrap">{viewTemplate.headerText}</p>}
                  <p className="text-sm text-gray-800 whitespace-pre-wrap">{viewTemplate.bodyText}</p>
                  {viewTemplate.footerText && <p className="text-xs text-gray-500 whitespace-pre-wrap">{viewTemplate.footerText}</p>}
                </div>
                {viewTemplate.metaStatus === 'REJECTED' && viewTemplate.metaRejectedReason && (
                  <div className="rounded-lg border border-red-200 bg-red-50 p-2 text-xs text-red-700">
                    <span className="font-semibold">Motivo del rechazo:</span> {viewTemplate.metaRejectedReason}
                  </div>
                )}
              </div>
              <div className="flex shrink-0 justify-end gap-2 border-t border-gray-100 px-6 py-3">
                {countVars(viewTemplate.bodyText) > 0 && (
                  <button type="button" onClick={() => { const t = viewTemplate; setViewTemplate(null); openMapping(t) }}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50">
                    <Variable size={15} /> Mapeo
                  </button>
                )}
                <button type="button" onClick={() => setViewTemplate(null)} className="rounded-lg bg-[#1a3a6b] px-4 py-1.5 text-sm font-semibold text-white hover:bg-[#234a85]">Cerrar</button>
              </div>
            </div>
          </div>
        )
      })()}

      {/* ── Modal: pedir el Excel de la campaña cuando falta estructura ── */}
      {showExcelModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 backdrop-blur-sm p-4"
          onClick={() => !processing && setShowExcelModal(false)}>
          <div className="w-full max-w-lg overflow-hidden rounded-2xl bg-white shadow-2xl"
            onClick={ev => ev.stopPropagation()}>
            {/* Encabezado con degradado — azul TalkIA */}
            <div className="relative bg-gradient-to-br from-[#1a3a6b] via-[#1e457e] to-[#2d5a9e] px-6 py-7 text-white">
              <button type="button" onClick={() => !processing && setShowExcelModal(false)}
                className="absolute right-4 top-4 rounded-full p-1 text-white/80 hover:bg-white/20 hover:text-white">
                <X size={18} />
              </button>
              <div className="flex items-center gap-3">
                <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-white/20 backdrop-blur">
                  <FileSpreadsheet size={26} />
                </div>
                <div>
                  <h3 className="text-lg font-bold leading-tight">Cargá tu archivo de campaña</h3>
                  <p className="text-sm text-white/85">Para mapear las variables de la plantilla</p>
                </div>
              </div>
            </div>

            {/* Cuerpo */}
            <div className="px-6 py-5 space-y-4">
              <p className="text-sm text-gray-600">
                Este maestro no tiene un proceso de descarga configurado, así que necesito conocer
                las <b>columnas reales</b> del archivo que vas a usar en la campaña. Subí el
                <b> Excel (o CSV)</b> de ejemplo y la IA mapeará cada variable
                <span className="mx-0.5 font-mono text-xs text-indigo-600">{'{{1}}, {{2}}…'}</span>
                a tus columnas.
              </p>

              {/* Zona de arrastrar y soltar */}
              <div
                onClick={() => !processing && fileInputRef.current?.click()}
                onDragOver={e => { e.preventDefault(); setIsDragging(true) }}
                onDragLeave={() => setIsDragging(false)}
                onDrop={onExcelDrop}
                className={`flex cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors ${
                  isDragging ? 'border-indigo-500 bg-indigo-50' : 'border-gray-300 hover:border-indigo-400 hover:bg-gray-50'
                } ${processing ? 'pointer-events-none opacity-60' : ''}`}>
                {processing ? (
                  <>
                    <Loader2 size={34} className="animate-spin text-indigo-600" />
                    <p className="text-sm font-medium text-gray-700">Leyendo columnas y generando…</p>
                  </>
                ) : (
                  <>
                    <div className="flex h-14 w-14 items-center justify-center rounded-full bg-indigo-100">
                      <UploadCloud size={28} className="text-indigo-600" />
                    </div>
                    <p className="text-sm font-semibold text-gray-800">Arrastrá tu archivo aquí</p>
                    <p className="text-xs text-gray-500">o <span className="font-medium text-indigo-600">hacé clic para elegir</span></p>
                    <p className="mt-1 text-[11px] text-gray-400">Formatos: .xlsx · .xls · .csv</p>
                  </>
                )}
              </div>

              <p className="text-xs text-gray-400">
                💡 Tip: solo se leen los <b>encabezados</b> (nombres de columna) y la primera fila como ejemplo. No se guarda el archivo.
              </p>
            </div>

            {/* Pie */}
            <div className="flex justify-end gap-2 border-t border-gray-100 px-6 py-4">
              <button type="button" onClick={() => !processing && setShowExcelModal(false)}
                disabled={processing}
                className="rounded-lg border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50">
                Cancelar
              </button>
              <button type="button" onClick={() => fileInputRef.current?.click()}
                disabled={processing}
                className="inline-flex items-center gap-1.5 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50">
                <UploadCloud size={16} /> Elegir archivo
              </button>
            </div>
          </div>
        </div>
      )}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold text-gray-900">Plantillas de Meta</h3>
          <p className="text-sm text-gray-500">
            Mensajes aprobados por Meta para iniciar campañas fuera de la ventana de 24h.
            El estado de aprobación de Meta es independiente del estado de la campaña.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* Generar con IA: solo visible dentro del formulario de plantilla
              (oculto en la vista de grid/listado). */}
          {showForm && (
            <button type="button" onClick={onGenerate}
              disabled={!campaignTemplateId || generateMut.isPending}
              className="inline-flex items-center gap-1.5 rounded-lg border border-purple-300 bg-purple-50 px-3 py-2 text-sm font-medium text-purple-700 hover:bg-purple-100 disabled:opacity-50"
              title={campaignTemplateId
                ? 'Genera borradores leyendo el prompt del maestro (una plantilla por burbuja ~)'
                : 'Guardá el maestro primero para poder generar desde su prompt'}>
              <Sparkles size={16} className={generateMut.isPending ? 'animate-pulse' : ''} /> Generar desde el prompt
            </button>
          )}
          <button type="button" onClick={onSyncAll} disabled={syncAllMut.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            title="Importar/actualizar las plantillas que ya existen en Meta">
            <RefreshCw size={16} className={syncAllMut.isPending ? 'animate-spin' : ''} /> Sincronizar con Meta
          </button>
          {!showForm && (
            <button type="button" onClick={startCreate}
              className="inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700">
              <Plus size={16} /> Nueva plantilla
            </button>
          )}
        </div>
      </div>

      {/* ── Formulario crear/editar ── */}
      {showForm && (
        <div className="rounded-xl border border-gray-200 bg-gray-50 p-4 space-y-3">
          <div className="flex items-center justify-between">
            <h4 className="font-medium text-gray-900">{isEditing ? 'Editar plantilla' : 'Nueva plantilla'}</h4>
            <button type="button" onClick={resetForm} className="text-gray-400 hover:text-gray-600"><X size={18} /></button>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <label className="block">
              <span className="text-xs font-medium text-gray-600">Nombre (se normaliza a snake_case)</span>
              <input value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                placeholder="recordatorio_pago"
                className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm" />
            </label>
            <label className="block">
              <span className="text-xs font-medium text-gray-600">Tipo de uso</span>
              <select value={form.purpose} onChange={e => setForm(f => ({ ...f, purpose: e.target.value as MetaTemplatePurpose }))}
                className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm">
                <option value="Launch">Lanzamiento (mensaje inicial)</option>
                <option value="FollowUp">Seguimiento</option>
              </select>
            </label>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <label className="block">
              <span className="text-xs font-medium text-gray-600">Idioma</span>
              <input value={form.language} onChange={e => setForm(f => ({ ...f, language: e.target.value }))}
                placeholder="es"
                className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm" />
            </label>
            <label className="block">
              <span className="flex items-center gap-1 text-xs font-medium text-gray-600">
                Categoría
                <button type="button" onClick={() => setShowCatHelp(v => !v)}
                  className="text-gray-400 hover:text-blue-600" title="¿Qué categoría elegir?">
                  <HelpCircle size={13} />
                </button>
              </span>
              <select value={form.category} onChange={e => setForm(f => ({ ...f, category: e.target.value as MetaTemplateCategory }))}
                className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm">
                {CATEGORIES.map(c => <option key={c.value} value={c.value}>{c.label}{c.recommended ? ' (recomendada)' : ''}</option>)}
              </select>
            </label>
          </div>

          {/* Ayuda de categoría: hint de la seleccionada + comparativa desplegable */}
          <div className="rounded-lg bg-blue-50/60 border border-blue-100 p-3 text-xs text-gray-600">
            <div className="flex items-start gap-2">
              <HelpCircle size={14} className="mt-0.5 shrink-0 text-blue-500" />
              <div className="flex-1">
                <p>
                  <span className="font-semibold text-gray-800">{CATEGORIES.find(c => c.value === form.category)?.label}:</span>{' '}
                  {CATEGORIES.find(c => c.value === form.category)?.hint}
                </p>
                <p className="mt-0.5 text-gray-500">
                  Ejemplos: {CATEGORIES.find(c => c.value === form.category)?.examples}
                </p>
                <button type="button" onClick={() => setShowCatHelp(v => !v)}
                  className="mt-1 font-medium text-blue-600 hover:text-blue-700">
                  {showCatHelp ? 'Ocultar comparación' : '¿Cuál elegir? Ver las 3 categorías'}
                </button>
              </div>
            </div>
            {showCatHelp && (
              <ul className="mt-2 space-y-2 border-t border-blue-100 pt-2">
                {CATEGORIES.map(c => (
                  <li key={c.value}>
                    <span className="font-semibold text-gray-800">{c.label}{c.recommended ? ' · recomendada para cobros' : ''}:</span>{' '}
                    <span className="text-gray-600">{c.hint}</span>
                    <span className="block text-gray-400">Ej.: {c.examples}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <label className="block">
            <span className="text-xs font-medium text-gray-600">Encabezado (opcional, máx. 1 variable {'{{1}}'})</span>
            <input value={form.headerText} onChange={e => setForm(f => ({ ...f, headerText: e.target.value }))}
              placeholder="Hola {{1}}"
              className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm" />
            <span className="mt-1 block text-[11px] text-gray-400">
              Texto plano: Meta no permite negritas ni formato (<span className="font-mono">* _ ~ `</span>) en el encabezado. El formato sí se permite en el cuerpo.
            </span>
          </label>
          {headerVars === 1 && (
            <label className="block pl-3">
              <span className="text-xs text-gray-500">Ejemplo para {'{{1}}'} del encabezado</span>
              <input value={form.headerSamples[0] ?? ''} onChange={e => setSample('header', 0, e.target.value)}
                placeholder="Juan"
                className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-1.5 text-sm" />
            </label>
          )}

          <label className="block">
            <span className="text-xs font-medium text-gray-600">Cuerpo * (usá {'{{1}}, {{2}}'}… para variables)</span>
            <textarea value={form.bodyText} onChange={e => setForm(f => ({ ...f, bodyText: e.target.value }))}
              rows={4} placeholder="Hola, tu póliza {{1}} tiene un saldo pendiente de {{2}}."
              className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm" />
          </label>
          {bodyVars > 0 && (
            <div className="pl-3 space-y-2">
              <span className="text-xs text-gray-500">
                Variables del cuerpo — asigná a cada <span className="font-mono">{'{{n}}'}</span> el <b>campo</b> (de dónde sale el dato al enviar) y un <b>ejemplo</b> (Meta lo exige):
              </span>
              <div className="space-y-2">
                {Array.from({ length: bodyVars }).map((_, i) => (
                  <div key={i} className="flex flex-wrap items-center gap-2">
                    <span className="w-10 shrink-0 font-mono text-xs font-semibold text-indigo-600">{`{{${i + 1}}}`}</span>
                    <select value={form.bodyMapping[i] ?? ''} onChange={e => setMapping(i, e.target.value)}
                      className="min-w-[10rem] flex-1 rounded-lg border border-gray-300 px-2 py-1.5 text-sm">
                      <option value="">— campo —</option>
                      {(availableFields ?? []).map(fld => <option key={fld} value={fld}>{fld}</option>)}
                    </select>
                    <input value={form.bodySamples[i] ?? ''} onChange={e => setSample('body', i, e.target.value)}
                      placeholder="ejemplo (ej: Juan)"
                      className="min-w-[8rem] flex-1 rounded-lg border border-gray-300 px-3 py-1.5 text-sm" />
                  </div>
                ))}
              </div>
            </div>
          )}

          <label className="block">
            <span className="text-xs font-medium text-gray-600">Pie (opcional, sin variables)</span>
            <input value={form.footerText} onChange={e => setForm(f => ({ ...f, footerText: e.target.value }))}
              placeholder="TalkIA · JAM Consulting"
              className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm" />
          </label>

          <div className="flex items-center justify-end gap-2 pt-1">
            <button type="button" onClick={() => submit(false)} disabled={createMut.isPending || updateMut.isPending}
              className="inline-flex items-center gap-1.5 rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50">
              <Save size={15} /> Guardar borrador
            </button>
            <button type="button" onClick={() => submit(true)} disabled={createMut.isPending || updateMut.isPending}
              className="inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              <Send size={15} /> Enviar a Meta
            </button>
          </div>
        </div>
      )}

      {/* ── Lista (oculta mientras el formulario está abierto, para no confundir) ── */}
      {!showForm && (isLoading ? (
        <p className="text-sm text-gray-500">Cargando plantillas…</p>
      ) : !templates || templates.length === 0 ? (
        <div className="rounded-xl border border-dashed border-gray-300 p-8 text-center text-sm text-gray-500">
          Aún no hay plantillas para esta línea. Creá una con “Nueva plantilla”, o usá
          “Sincronizar con Meta” para importar las que ya existen en tu cuenta de Meta.
        </div>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-gray-200">
          {(() => {
            // Tamaño de cada grupo de burbujas (para mostrar "N de M").
            const groupSizes = new Map<string, number>()
            for (const t of templates)
              if (t.bubbleGroupId) groupSizes.set(t.bubbleGroupId, (groupSizes.get(t.bubbleGroupId) ?? 0) + 1)
            return (
              <table className="min-w-full divide-y divide-gray-100 text-sm">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-3 py-2 text-left text-xs font-medium text-gray-500">Plantilla</th>
                    <th className="px-3 py-2 text-left text-xs font-medium text-gray-500">Tipo de uso</th>
                    <th className="px-3 py-2 text-left text-xs font-medium text-gray-500">Idioma · Categoría</th>
                    <th className="px-3 py-2 text-left text-xs font-medium text-gray-500">Estado</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500">Acciones</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 bg-white">
                  {templates.map(t => {
                    const sm = STATUS_META[t.metaStatus] ?? STATUS_META.DRAFT
                    const groupSize = t.bubbleGroupId ? groupSizes.get(t.bubbleGroupId) ?? 1 : 1
                    const varCount = countVars(t.bodyText)
                    const mappedCount = t.bodyMapping?.filter(Boolean).length ?? 0
                    const mappingIncomplete = varCount > 0 && mappedCount < varCount
                    const isDraftOrRejected = t.metaStatus === 'DRAFT' || t.metaStatus === 'REJECTED'
                    return (
                      <tr key={t.id} className="hover:bg-gray-50/60">
                        {/* Plantilla + badge de secuencia */}
                        <td className="px-3 py-2 align-top">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <span className="font-mono text-sm font-semibold text-gray-900">{t.name}</span>
                            {groupSize > 1 && (
                              <span className="inline-flex items-center rounded-full bg-indigo-100 text-indigo-700 px-2 py-0.5 text-[11px] font-medium"
                                title="Se envían en secuencia en la campaña (replican las burbujas del prompt)">
                                Burbuja {t.sequenceOrder} de {groupSize}
                              </span>
                            )}
                          </div>
                        </td>
                        {/* Tipo de uso (Lanzamiento / Seguimiento) */}
                        <td className="px-3 py-2 align-top">
                          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium ${t.purpose === 'FollowUp' ? 'bg-teal-100 text-teal-700' : 'bg-violet-100 text-violet-700'}`}>
                            {t.purpose === 'FollowUp' ? 'Seguimiento' : 'Lanzamiento'}
                          </span>
                        </td>
                        {/* Idioma · Categoría */}
                        <td className="px-3 py-2 align-top whitespace-nowrap text-xs text-gray-500">
                          {t.language} · {t.category}
                        </td>
                        {/* Estado */}
                        <td className="px-3 py-2 align-top">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-medium ${sm.cls}`}>
                              <sm.Icon size={12} /> {sm.label}
                            </span>
                            <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium ${t.isEnabled ? 'bg-blue-50 text-blue-700' : 'bg-gray-100 text-gray-500'}`}>
                              {t.isEnabled ? 'Activa' : 'Inactiva'}
                            </span>
                            {mappingIncomplete && (
                              <span className="inline-flex items-center rounded-full bg-amber-100 text-amber-700 px-2 py-0.5 text-[11px] font-medium" title="Faltan variables por mapear">
                                Mapeo {mappedCount}/{varCount}
                              </span>
                            )}
                          </div>
                        </td>
                        {/* Acciones */}
                        <td className="px-3 py-2 align-top">
                          <div className="flex items-center justify-end gap-1">
                            <button type="button" title="Ver plantilla" onClick={() => setViewTemplate(t)}
                              className="rounded-lg p-2 text-gray-400 hover:bg-gray-100 hover:text-[#1a3a6b]">
                              <Eye size={16} />
                            </button>
                            {isDraftOrRejected && (
                              <button type="button" onClick={() => onSubmit(t)} disabled={submitMut.isPending}
                                className="inline-flex items-center gap-1.5 rounded-lg bg-green-600 px-2.5 py-1.5 text-xs font-semibold text-white hover:bg-green-700 disabled:opacity-50"
                                title="Enviar a Meta para aprobación">
                                <Send size={14} /> Enviar
                              </button>
                            )}
                            {varCount > 0 && (
                              <button type="button" title="Mapeo de variables {{n}}→campo" onClick={() => openMapping(t)}
                                className={`inline-flex items-center gap-1 rounded-lg px-2 py-1.5 text-xs font-medium ${mappingIncomplete ? 'bg-amber-100 text-amber-700 hover:bg-amber-200' : 'text-gray-600 hover:bg-gray-100'}`}>
                                <Variable size={14} /> Mapeo
                              </button>
                            )}
                            <button type="button" title="Sincronizar estado con Meta" onClick={() => onSync(t)} disabled={syncMut.isPending}
                              className="rounded-lg p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-700 disabled:opacity-50">
                              <RefreshCw size={16} className={syncMut.isPending ? 'animate-spin' : ''} />
                            </button>
                            <button type="button" title={t.isEnabled ? 'Desactivar' : 'Activar'}
                              onClick={() => toggleMut.mutate({ id: t.id, enable: !t.isEnabled })}
                              className={`rounded-lg p-2 hover:bg-gray-100 ${t.isEnabled ? 'text-gray-400 hover:text-gray-700' : 'text-blue-500 hover:text-blue-700'}`}>
                              <Power size={16} />
                            </button>
                            {isDraftOrRejected && (
                              <button type="button" title="Editar" onClick={() => startEdit(t)}
                                className="rounded-lg p-2 text-gray-400 hover:bg-gray-100 hover:text-gray-700">
                                <Pencil size={16} />
                              </button>
                            )}
                            <button type="button" title="Eliminar" onClick={() => onDelete(t)}
                              className="rounded-lg p-2 text-gray-400 hover:bg-red-50 hover:text-red-600">
                              <Trash2 size={16} />
                            </button>
                          </div>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            )
          })()}
        </div>
      ))}
    </div>
  )
}
