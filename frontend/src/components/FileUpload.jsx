import { useRef, useState } from 'react'
import { UploadIcon } from './icons.jsx'

const ALLOWED = ['.txt', '.md']

function hasAllowedExt(name) {
  const lower = name.toLowerCase()
  return ALLOWED.some((ext) => lower.endsWith(ext))
}

// Drag-and-drop / click dropzone. Validates extension client-side (the backend
// enforces .txt/.md too) and hands a valid File up to the parent.
export default function FileUpload({ onFile, disabled }) {
  const inputRef = useRef(null)
  const [dragging, setDragging] = useState(false)
  const [error, setError] = useState('')

  function accept(file) {
    if (!file) return
    if (!hasAllowedExt(file.name)) {
      setError('Only .txt and .md files are supported.')
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
        <div className="dropzone-hint">CLICK_OR_DRAG · FORMAT: .TXT / .MD</div>
        <input
          ref={inputRef}
          type="file"
          accept=".txt,.md,text/plain,text/markdown"
          style={{ display: 'none' }}
          onChange={(e) => accept(e.target.files?.[0])}
          disabled={disabled}
        />
      </div>
      {error && <div className="error-line">// ERROR: {error}</div>}
    </div>
  )
}
