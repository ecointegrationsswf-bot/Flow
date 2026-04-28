import { useCallback, useEffect, useRef, useState } from 'react'
import { FileText, Upload, Trash2, AlertCircle, Eye, CheckCircle2, X, Pencil, Check, Info, AlertTriangle } from 'lucide-react'
import {
  useCampaignTemplateDocuments,
  useUploadCampaignTemplateDocument,
  useUpdateCampaignTemplateDocumentDescription,
  useDeleteCampaignTemplateDocument,
} from '@/shared/hooks/useCampaignTemplateDocuments'
import { useTenant } from '@/shared/hooks/useTenant'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { PdfViewerModal } from '@/shared/components/PdfViewerModal'

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function CampaignTemplateDocumentsSection({ templateId }: { templateId: string }) {
  const { data: docs, isLoading } = useCampaignTemplateDocuments(templateId)
  const { data: tenant } = useTenant()
  const uploadMutation = useUploadCampaignTemplateDocument(templateId)
  const updateDescMutation = useUpdateCampaignTemplateDocumentDescription(templateId)
  const deleteMutation = useDeleteCampaignTemplateDocument(templateId)

  const referenceDocsEnabled = tenant?.referenceDocumentsEnabled ?? false

  const fileRef = useRef<HTMLInputElement>(null)
  const [dragOver, setDragOver] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<{ id: string; name: string } | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [previewDoc, setPreviewDoc] = useState<{
    id: string
    fileName: string
    fileSizeBytes: number
  } | null>(null)
  // Cola de archivos pendientes de descripción. Se procesan uno a uno:
  // el primer elemento es el que actualmente muestra el panel azul.
  const [pendingQueue, setPendingQueue] = useState<File[]>([])
  const [pendingDescription, setPendingDescription] = useState('')
  const pendingFile = pendingQueue[0] ?? null
  // Edición in-line de descripción por docId.
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingText, setEditingText] = useState('')

  useEffect(() => {
    if (!successMessage) return
    const id = setTimeout(() => setSuccessMessage(null), 3000)
    return () => clearTimeout(id)
  }, [successMessage])

  const handleFiles = useCallback(
    (files: FileList | null) => {
      if (!files || files.length === 0) return
      // Encolamos TODOS los PDFs seleccionados. El usuario ingresa la descripción
      // de cada uno en orden antes de que se suban.
      const pdfs = Array.from(files).filter((f) => f.type === 'application/pdf')
      if (pdfs.length === 0) return
      setPendingQueue((prev) => [...prev, ...pdfs])
      // Solo reseteamos la descripción si empezamos una cola nueva — evita
      // perder lo que el usuario venía escribiendo para el archivo actual.
      setPendingDescription((prev) => (pendingQueue.length === 0 ? '' : prev))
    },
    [pendingQueue.length],
  )

  const confirmUpload = () => {
    if (!pendingFile) return
    const desc = pendingDescription.trim()
    if (desc.length === 0) return
    const fileName = pendingFile.name
    uploadMutation.mutate(
      { file: pendingFile, description: desc },
      {
        onSuccess: () => {
          setSuccessMessage(`Documento "${fileName}" subido.`)
          // Avanzar al siguiente archivo de la cola.
          setPendingQueue((prev) => prev.slice(1))
          setPendingDescription('')
        },
      },
    )
  }

  const cancelPending = () => {
    // Descarta SÓLO el archivo actual; el resto de la cola permanece.
    setPendingQueue((prev) => prev.slice(1))
    setPendingDescription('')
  }

  const discardAllPending = () => {
    setPendingQueue([])
    setPendingDescription('')
  }

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault()
      setDragOver(false)
      handleFiles(e.dataTransfer.files)
    },
    [handleFiles],
  )

  return (
    <div>
      <h2 className="mb-4 text-sm font-semibold text-gray-900">Documentos de referencia</h2>
      <p className="mb-3 text-xs text-gray-500">
        Sube archivos PDF que el agente usara como contexto para responder en esta campana. Maximo 10 MB por archivo · 5 documentos · 20 MB totales.
      </p>

      {/* Banner de estado del feature flag */}
      {referenceDocsEnabled ? (
        <div className="mb-4 flex items-start gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3">
          <Info className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
          <div className="text-xs text-emerald-800">
            <p className="font-medium">Inyección activa para este cliente.</p>
            <p className="mt-0.5">
              Los PDFs cargados acá se envían al agente durante las conversaciones de este maestro. El agente los
              consulta cuando su prompt base no cubre la pregunta del cliente.
            </p>
          </div>
        </div>
      ) : (
        <div className="mb-4 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
          <div className="text-xs text-amber-800">
            <p className="font-medium">La funcionalidad está deshabilitada para este cliente.</p>
            <p className="mt-0.5">
              Podés cargar PDFs aquí pero el agente <strong>no los leerá</strong> durante las conversaciones hasta que
              se active "Documentos de Referencia" en Configuración → Tenant.
            </p>
          </div>
        </div>
      )}

      {/* Drop zone — oculto mientras hay un archivo pendiente de descripción */}
      {!pendingFile && (
        <div
          onDragOver={(e) => {
            e.preventDefault()
            setDragOver(true)
          }}
          onDragLeave={() => setDragOver(false)}
          onDrop={onDrop}
          onClick={() => fileRef.current?.click()}
          className={`flex cursor-pointer flex-col items-center gap-2 rounded-lg border-2 border-dashed p-6 transition-colors ${
            dragOver
              ? 'border-blue-400 bg-blue-50'
              : 'border-gray-300 hover:border-gray-400 hover:bg-gray-50'
          }`}
        >
          <Upload className="h-8 w-8 text-gray-400" />
          <p className="text-sm text-gray-600">
            Arrastra archivos PDF aqui o <span className="font-medium text-blue-600">selecciona</span>
          </p>
          <p className="text-xs text-gray-400">Solo archivos PDF, maximo 10 MB</p>
          <input
            ref={fileRef}
            type="file"
            accept=".pdf,application/pdf"
            multiple
            className="hidden"
            onChange={(e) => {
              handleFiles(e.target.files)
              e.target.value = ''
            }}
          />
        </div>
      )}

      {/* Panel de archivo pendiente — pide descripción obligatoria antes de subir */}
      {pendingFile && (
        <div className="rounded-lg border-2 border-blue-300 bg-blue-50 p-4">
          {pendingQueue.length > 1 && (
            <div className="mb-2 flex items-center justify-between text-xs font-medium text-blue-800">
              <span>Documento 1 de {pendingQueue.length} · faltan {pendingQueue.length - 1} después de éste</span>
              <button
                type="button"
                onClick={discardAllPending}
                disabled={uploadMutation.isPending}
                className="text-red-600 hover:underline disabled:opacity-50"
              >
                Descartar todos
              </button>
            </div>
          )}
          <div className="mb-3 flex items-center gap-3">
            <FileText className="h-6 w-6 shrink-0 text-red-500" />
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-gray-900">{pendingFile.name}</p>
              <p className="text-xs text-gray-500">{formatSize(pendingFile.size)} · Pendiente de descripción</p>
            </div>
            <button
              type="button"
              onClick={cancelPending}
              className="rounded-lg p-1.5 text-gray-400 hover:bg-white hover:text-gray-600"
              title="Descartar este archivo"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
          <label className="mb-1 block text-xs font-medium text-gray-700">
            Descripción <span className="text-red-500">*</span> — ayuda al agente a saber cuándo consultar el documento
          </label>
          <input
            type="text"
            autoFocus
            value={pendingDescription}
            onChange={(e) => setPendingDescription(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && pendingDescription.trim().length > 0) {
                confirmUpload()
              } else if (e.key === 'Escape') {
                cancelPending()
              }
            }}
            placeholder="Ej: Cobertura de accidentes personales del plan 2026"
            maxLength={500}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
          <div className="mt-3 flex items-center justify-end gap-2">
            <button
              type="button"
              onClick={cancelPending}
              disabled={uploadMutation.isPending}
              className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={confirmUpload}
              disabled={uploadMutation.isPending || pendingDescription.trim().length === 0}
              className="flex items-center gap-2 rounded-md bg-blue-600 px-4 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {uploadMutation.isPending && <span className="h-3 w-3 animate-spin rounded-full border-2 border-white/50 border-t-white" />}
              {pendingQueue.length > 1 ? 'Subir y continuar con el siguiente' : 'Subir documento'}
            </button>
          </div>
        </div>
      )}

      {/* Upload status */}
      {uploadMutation.isPending && (
        <p className="mt-3 text-xs text-blue-600">Subiendo documento...</p>
      )}
      {uploadMutation.isError && (
        <div className="mt-3 flex items-center gap-1.5 text-xs text-red-600">
          <AlertCircle className="h-3.5 w-3.5" />
          <span>Error al subir el documento. Verifica que sea un PDF valido de menos de 10 MB.</span>
        </div>
      )}

      {/* Documents list */}
      {isLoading ? (
        <p className="mt-4 text-xs text-gray-400">Cargando documentos...</p>
      ) : docs && docs.length > 0 ? (
        <ul className="mt-4 divide-y divide-gray-100">
          {docs.map((doc) => (
            <li key={doc.id} className="flex flex-col gap-2 py-3">
              <div className="flex items-center justify-between gap-3">
                <button
                  type="button"
                  onClick={() =>
                    setPreviewDoc({
                      id: doc.id,
                      fileName: doc.fileName,
                      fileSizeBytes: doc.fileSizeBytes,
                    })
                  }
                  className="flex items-center gap-3 min-w-0 text-left group flex-1"
                >
                  <FileText className="h-5 w-5 shrink-0 text-red-500" />
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium text-gray-900 group-hover:text-blue-600 transition-colors">
                      {doc.fileName}
                    </p>
                    <p className="text-xs text-gray-400">
                      {formatSize(doc.fileSizeBytes)} — {new Date(doc.uploadedAt).toLocaleDateString('es-PA')}
                    </p>
                  </div>
                </button>
                <div className="flex items-center gap-1 shrink-0">
                  <button
                    type="button"
                    onClick={() => {
                      setEditingId(doc.id)
                      setEditingText(doc.description ?? '')
                    }}
                    disabled={updateDescMutation.isPending}
                    className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-indigo-600 disabled:opacity-50 transition-colors"
                    title="Editar descripción"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    onClick={() =>
                      setPreviewDoc({
                        id: doc.id,
                        fileName: doc.fileName,
                        fileSizeBytes: doc.fileSizeBytes,
                      })
                    }
                    className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                    title="Ver documento"
                  >
                    <Eye className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    onClick={() => setDeleteTarget({ id: doc.id, name: doc.fileName })}
                    disabled={deleteMutation.isPending}
                    className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-red-600 disabled:opacity-50 transition-colors"
                  title="Eliminar"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
                </div>
              </div>

              {/* Descripción — visible o en edición */}
              {editingId === doc.id ? (
                <div className="flex items-center gap-2 pl-8">
                  <input
                    type="text"
                    autoFocus
                    value={editingText}
                    onChange={(e) => setEditingText(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') {
                        updateDescMutation.mutate(
                          { docId: doc.id, description: editingText.trim() || null },
                          {
                            onSuccess: () => {
                              setEditingId(null)
                              setSuccessMessage(`Descripción de "${doc.fileName}" actualizada.`)
                            },
                          },
                        )
                      } else if (e.key === 'Escape') {
                        setEditingId(null)
                      }
                    }}
                    maxLength={500}
                    placeholder="Ej: Cobertura de accidentes personales del plan 2026"
                    className="flex-1 rounded-md border border-gray-300 px-2 py-1 text-xs focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  />
                  <button
                    type="button"
                    onClick={() => {
                      updateDescMutation.mutate(
                        { docId: doc.id, description: editingText.trim() || null },
                        {
                          onSuccess: () => {
                            setEditingId(null)
                            setSuccessMessage(`Descripción de "${doc.fileName}" actualizada.`)
                          },
                        },
                      )
                    }}
                    disabled={updateDescMutation.isPending}
                    className="rounded p-1 text-emerald-600 hover:bg-emerald-50 disabled:opacity-50"
                    title="Guardar"
                  >
                    <Check className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    onClick={() => setEditingId(null)}
                    className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
                    title="Cancelar"
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>
              ) : doc.description ? (
                <p className="pl-8 text-xs italic text-gray-600">{doc.description}</p>
              ) : (
                <p className="pl-8 text-xs italic text-gray-400">Sin descripción — agregar una ayuda al agente a decidir cuándo consultar el documento.</p>
              )}
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-4 text-center text-xs text-gray-400">
          No hay documentos asociados a este maestro de campana.
        </p>
      )}

      {/* PDF Viewer */}
      <PdfViewerModal
        open={!!previewDoc}
        onClose={() => setPreviewDoc(null)}
        previewPath={
          previewDoc
            ? `/campaign-templates/${templateId}/documents/${previewDoc.id}/preview`
            : ''
        }
        downloadPath={
          previewDoc
            ? `/campaign-templates/${templateId}/documents/${previewDoc.id}/download`
            : ''
        }
        fileName={previewDoc?.fileName ?? ''}
        fileSize={previewDoc ? formatSize(previewDoc.fileSizeBytes) : undefined}
      />

      {/* Confirm delete */}
      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => {
          const target = deleteTarget
          if (!target) return
          setDeleteTarget(null)
          setDeleteError(null)
          deleteMutation.mutate(target.id, {
            onSuccess: () => setSuccessMessage(`Documento "${target.name}" eliminado.`),
            onError: () => setDeleteError(`No se pudo eliminar "${target.name}". Intenta de nuevo.`),
          })
        }}
        title="Eliminar documento"
        description={`Se eliminara permanentemente "${deleteTarget?.name}". Esta accion no se puede deshacer.`}
        confirmLabel="Eliminar"
        variant="danger"
      />

      {/* Banner de confirmación inline — no navega ni cierra la pestaña. */}
      {successMessage && (
        <div className="fixed bottom-6 right-6 z-50 flex items-start gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 shadow-lg">
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
          <p className="text-sm text-emerald-800">{successMessage}</p>
          <button
            onClick={() => setSuccessMessage(null)}
            className="ml-2 rounded p-0.5 text-emerald-600 hover:bg-emerald-100"
            aria-label="Cerrar"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}
      {deleteError && (
        <div className="fixed bottom-6 right-6 z-50 flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-4 py-3 shadow-lg">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-red-600" />
          <p className="text-sm text-red-800">{deleteError}</p>
          <button
            onClick={() => setDeleteError(null)}
            className="ml-2 rounded p-0.5 text-red-600 hover:bg-red-100"
            aria-label="Cerrar"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}
    </div>
  )
}
