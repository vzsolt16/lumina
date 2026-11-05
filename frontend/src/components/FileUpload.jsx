import { useRef, useState } from 'react'
import { UploadIcon } from './icons.jsx'
import {
  SUPPORTED_DOCUMENT_ACCEPT,
  SUPPORTED_DOCUMENT_EXTENSIONS,
  supportedFormatsLabel,
} from '../constants/documentFormats.js'

function hasAllowedExt(name) {
  const lower = name.toLowerCase()
  return SUPPORTED_DOCUMENT_EXTENSIONS.some((ext) => lower.endsWith(ext))
}

// Drag-and-drop / click dropzone. Validates extension client-side (the backend
// enforces the same list) and hands a valid File up to the parent.
export default function FileUpload({ onFile, disabled }) {
  const inputRef = useRef(null)
  const [dragging, setDragging] = useState(false)
  const [error, setError] = useState('')

  function accept(file) {
    if (!file) return
    if (!hasAllowedExt(file.name)) {
      setError(`Supported file types: ${SUPPORTED_DOCUMENT_EXTENSIONS.join(', ')}`)
      return
    }
    setError('')
    onFile(file)
  }

  function onDrop(e) {
    e.preventDefault()
    setDragging(false)
    if (disabled) return
    accept(e.dataTransfer.files?.[0])
  }

  return (
    <div>
      <div
        className={`dropzone${dragging ? ' dragging' : ''}`}
        onClick={() => !disabled && inputRef.current?.click()}
        onDragOver={(e) => {
          e.preventDefault()
          if (!disabled) setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => {
          if ((e.key === 'Enter' || e.key === ' ') && !disabled) {
            inputRef.current?.click()
          }
        }}
      >
        <div className="dropzone-icon"><UploadIcon size={28} /></div>
        <div className="dropzone-title">Drop your notes here</div>
        <div className="dropzone-hint">CLICK_OR_DRAG · FORMAT: {supportedFormatsLabel()}</div>
        <input
          ref={inputRef}
          type="file"
          accept={SUPPORTED_DOCUMENT_ACCEPT}
          style={{ display: 'none' }}
          onChange={(e) => accept(e.target.files?.[0])}
          disabled={disabled}
        />
      </div>
      {error && <div className="error-line">// ERROR: {error}</div>}
    </div>
  )
}
