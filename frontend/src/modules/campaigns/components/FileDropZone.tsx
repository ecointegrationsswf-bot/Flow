import { useRef, useState } from 'react'
import { Upload, File, X } from 'lucide-react'

interface FileDropZoneProps {
  accept: string
  onFileSelect: (file: File) => void
  selectedFile: File | null
  onClear: () => void
}

export function FileDropZone({ accept, onFileSelect, selectedFile, onClear }: FileDropZoneProps) {
  const [isDragging, setIsDragging] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    const file = e.dataTransfer.files[0]
    if (file) onFileSelect(file)
  }

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  if (selectedFile) {
    return (
      <div className="flex items-center justify-between rounded-lg border border-green-200 bg-green-50 p-4">
        <div className="flex items-center gap-3">
          <File className="h-8 w-8 text-green-600" />
          <div>
            <p className="text-sm font-medium text-gray-900">{selectedFile.name}</p>
            <p className="text-xs text-gray-500">{formatSize(selectedFile.size)}</p>
          </div>
        </div>
        <button onClick={onClear} className="rounded-md p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600">
          <X className="h-4 w-4" />
        </button>
      </div>
    )
  }

  return (
    <div
      onDragOver={(e) => { e.preventDefault(); setIsDragging(true) }}
      onDragLeave={() => setIsDragging(false)}
      onDrop={handleDrop}
      onClick={() => inputRef.current?.click()}
      className={`cursor-pointer rounded-lg border-2 border-dashed p-8 text-center transition-colors ${
        isDragging ? 'border-blue-400 bg-blue-50' : 'border-gray-300 hover:border-gray-400'
      }`}
    >
      <Upload className="mx-auto h-8 w-8 text-gray-400" />
      <p className="mt-2 text-sm font-medium text-gray-700">
        Arrastra tu archivo aqui o haz clic para seleccionar
      </p>
      <p className="mt-1 text-xs text-gray-500">CSV, Excel (.xlsx, .xls)</p>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        onChange={(e) => { if (e.target.files?.[0]) onFileSelect(e.target.files[0]) }}
        className="hidden"
      />
    </div>
  )
}
