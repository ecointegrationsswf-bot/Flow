import { useEffect, useRef, useState } from 'react'
import { X, Download, ZoomIn, ZoomOut, Maximize2, Loader2, AlertCircle } from 'lucide-react'
import { api } from '@/shared/api/client'

interface PdfViewerModalProps {
  open: boolean
  onClose: () => void
  /**
   * Path relativo al API base para obtener el PDF (preview inline).
   * El modal se encarga de hacer fetch via axios (con auth + tenant header),
   * crear un blob URL y pasarlo al iframe. Esto evita problemas de cross-origin
   * y mixed content que tiene un iframe apuntando directo al backend.
   */
  previewPath: string
  /** Path para descarga como attachment (mismo patrón que previewPath). */
  downloadPath: string
  fileName: string
  fileSize?: string
}

export function PdfViewerModal({
  open,
  onClose,
  previewPath,
  downloadPath,
  fileName,
  fileSize,
}: PdfViewerModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const [loading, setLoading] = useState(true)
  const [scale, setScale] = useState(100)
  const [blobUrl, setBlobUrl] = useState<string>('')
  const [error, setError] = useState<string>('')

  useEffect(() => {
    const el = dialogRef.current
    if (!el) return
    if (open && !el.open) {
      setLoading(true)
      setError('')
      setScale(100)
      el.showModal()
    } else if (!open && el.open) {
      el.close()
    }
  }, [open])

  // Cuando se abre con un nuevo path, descargamos el PDF via axios (lleva auth
  // + tenant header) y lo convertimos a blob URL. El iframe lo consume desde
  // el mismo origin y sin problemas de esquema.
  useEffect(() => {
    if (!open || !previewPath) return
    let cancelled = false
    let currentBlobUrl = ''

    setLoading(true)
    setError('')
    setBlobUrl('')

    api
      .get(previewPath, { responseType: 'blob' })
      .then((res) => {
        if (cancelled) return
        const blob = new Blob([res.data], { type: 'application/pdf' })
        currentBlobUrl = URL.createObjectURL(blob)
        setBlobUrl(currentBlobUrl)
        setLoading(false)
      })
      .catch((err) => {
        if (cancelled) return
        console.error('[PdfViewer] error descargando PDF', err)
        setError('No se pudo cargar el documento. Intenta descargarlo.')
        setLoading(false)
      })

    return () => {
      cancelled = true
      if (currentBlobUrl) URL.revokeObjectURL(currentBlobUrl)
    }
  }, [open, previewPath])

  // Cerrar con Escape
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && open) onClose()
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [open, onClose])

  const handleDownload = async () => {
    try {
      const res = await api.get(downloadPath, { responseType: 'blob' })
      const blob = new Blob([res.data], { type: 'application/pdf' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = fileName
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch (e) {
      console.error('[PdfViewer] error descargando', e)
    }
  }

  const zoomIn = () => setScale((s) => Math.min(s + 25, 200))
  const zoomOut = () => setScale((s) => Math.max(s - 25, 50))
  const zoomFit = () => setScale(100)

  return (
    <dialog
      ref={dialogRef}
      onClose={onClose}
      className="m-0 h-screen w-screen max-h-screen max-w-full rounded-none border-none bg-transparent p-0 backdrop:bg-black/60"
    >
      <div className="flex h-full w-full flex-col bg-gray-900">
        {/* Toolbar */}
        <div className="flex items-center justify-between border-b border-gray-700 bg-gray-800 px-4 py-2.5">
          <div className="flex items-center gap-3 min-w-0">
            <div className="min-w-0">
              <h3 className="truncate text-sm font-medium text-white">{fileName}</h3>
              {fileSize && (
                <p className="text-xs text-gray-400">{fileSize}</p>
              )}
            </div>
          </div>

          <div className="flex items-center gap-1">
            {/* Zoom controls */}
            <button
              onClick={zoomOut}
              disabled={scale <= 50}
              className="rounded p-1.5 text-gray-400 hover:bg-gray-700 hover:text-white disabled:opacity-30"
              title="Reducir"
            >
              <ZoomOut className="h-4 w-4" />
            </button>
            <span className="min-w-[3rem] text-center text-xs text-gray-400">{scale}%</span>
            <button
              onClick={zoomIn}
              disabled={scale >= 200}
              className="rounded p-1.5 text-gray-400 hover:bg-gray-700 hover:text-white disabled:opacity-30"
              title="Ampliar"
            >
              <ZoomIn className="h-4 w-4" />
            </button>
            <button
              onClick={zoomFit}
              className="rounded p-1.5 text-gray-400 hover:bg-gray-700 hover:text-white"
              title="Ajustar"
            >
              <Maximize2 className="h-4 w-4" />
            </button>

            <div className="mx-2 h-5 w-px bg-gray-600" />

            {/* Download */}
            <button
              onClick={handleDownload}
              className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 transition-colors"
            >
              <Download className="h-3.5 w-3.5" />
              Descargar
            </button>

            <div className="mx-2 h-5 w-px bg-gray-600" />

            {/* Close */}
            <button
              onClick={onClose}
              className="rounded p-1.5 text-gray-400 hover:bg-gray-700 hover:text-white"
              title="Cerrar"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
        </div>

        {/* PDF content */}
        <div className="relative flex-1 overflow-auto bg-gray-900">
          {loading && (
            <div className="absolute inset-0 z-10 flex items-center justify-center bg-gray-900">
              <div className="flex flex-col items-center gap-3">
                <Loader2 className="h-8 w-8 animate-spin text-blue-500" />
                <p className="text-sm text-gray-400">Cargando documento...</p>
              </div>
            </div>
          )}
          {error && !loading && (
            <div className="absolute inset-0 z-10 flex items-center justify-center bg-gray-900">
              <div className="flex flex-col items-center gap-3 text-center">
                <AlertCircle className="h-8 w-8 text-red-500" />
                <p className="text-sm text-gray-300">{error}</p>
              </div>
            </div>
          )}
          {!loading && !error && blobUrl && (
            <div
              className="flex min-h-full items-start justify-center p-4"
              style={{ transform: `scale(${scale / 100})`, transformOrigin: 'top center' }}
            >
              <iframe
                src={blobUrl}
                title={fileName}
                className="h-[calc(100vh-60px)] w-full max-w-4xl rounded bg-white shadow-2xl"
                style={{
                  minHeight: scale > 100 ? `${100 / (scale / 100)}vh` : undefined,
                }}
              />
            </div>
          )}
        </div>
      </div>
    </dialog>
  )
}
