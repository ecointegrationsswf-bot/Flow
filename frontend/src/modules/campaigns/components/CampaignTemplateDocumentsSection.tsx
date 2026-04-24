import { useCallback, useRef, useState } from 'react'
import { FileText, Upload, Trash2, AlertCircle, Eye } from 'lucide-react'
import {
  useCampaignTemplateDocuments,
  useUploadCampaignTemplateDocument,
  useDeleteCampaignTemplateDocument,
} from '@/shared/hooks/useCampaignTemplateDocuments'
import { ConfirmDialog } from '@/shared/components/ConfirmDialog'
import { PdfViewerModal } from '@/shared/components/PdfViewerModal'

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function CampaignTemplateDocumentsSection({ templateId }: { templateId: string }) {
  const { data: docs, isLoading } = useCampaignTemplateDocuments(templateId)
  const uploadMutation = useUploadCampaignTemplateDocument(templateId)
  const deleteMutation = useDeleteCampaignTemplateDocument(templateId)

  const fileRef = useRef<HTMLInputElement>(null)
  const [dragOver, setDragOver] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<{ id: string; name: string } | null>(null)
  const [previewDoc, setPreviewDoc] = useState<{
    id: string
    fileName: string
    fileSizeBytes: number
  } | null>(null)

  const handleFiles = useCallback(
    (files: FileList | null) => {
      if (!files) return
      Array.from(files).forEach((f) => {
        if (f.type === 'application/pdf') uploadMutation.mutate(f)
      })
    },
    [uploadMutation],
  )

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
      <p className="mb-4 text-xs text-gray-500">
        Sube archivos PDF que el agente usara como contexto para responder en esta campana. Maximo 10 MB por archivo.
      </p>

      {/* Drop zone */}
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
            <li key={doc.id} className="flex items-center justify-between py-3">
              <button
                type="button"
                onClick={() =>
                  setPreviewDoc({
                    id: doc.id,
                    fileName: doc.fileName,
                    fileSizeBytes: doc.fileSizeBytes,
                  })
                }
                className="flex items-center gap-3 min-w-0 text-left group"
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
          if (deleteTarget) deleteMutation.mutate(deleteTarget.id)
          setDeleteTarget(null)
        }}
        title="Eliminar documento"
        description={`Se eliminara permanentemente "${deleteTarget?.name}". Esta accion no se puede deshacer.`}
        confirmLabel="Eliminar"
        variant="danger"
      />
    </div>
  )
}
