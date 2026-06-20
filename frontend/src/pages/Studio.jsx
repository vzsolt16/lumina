import { useEffect, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import Nav from '../components/Nav.jsx'
import Footer from '../components/Footer.jsx'
import FileUpload from '../components/FileUpload.jsx'
import { FileIcon } from '../components/icons.jsx'
import { getDocuments, uploadDocument } from '../api/client.js'
import './Studio.css'

function formatDate(value) {
  if (!value) return ''
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString()
}

export default function Studio() {
  const navigate = useNavigate()
  const [docs, setDocs] = useState(null) // null = loading
  const [loadError, setLoadError] = useState('')
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState('')

  // Auto-fetch the user's documents on entry.
  useEffect(() => {
    let cancelled = false
    getDocuments()
      .then((list) => {
        if (!cancelled) setDocs(Array.isArray(list) ? list : [])
      })
      .catch((err) => {
        if (!cancelled) {
          setDocs([])
          setLoadError(err?.message || 'Could not load documents.')
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  async function handleFile(file) {
    setUploading(true)
    setUploadError('')
    try {
      const result = await uploadDocument(file)
      navigate(`/studio/${result.id}`)
    } catch (err) {
      setUploadError(err?.message || 'Upload failed.')
      setUploading(false)
    }
  }

  return (
    <>
      <Nav showSectionLinks={false} />

      <div className="studio">
        <div className="studio-head">
          <div className="section-tag">Studio · Library</div>
          <div className="section-heading">Your<br />documents</div>
          <div className="section-sub">
            — Pick a document to study, or upload new material
          </div>
        </div>

        {/* UPLOAD */}
        <div className="panel">
          <div className="panel-bar">
            <div className="panel-title">Input · Upload material</div>
            <div className="panel-status">
              {uploading ? 'UPLOADING' : 'AWAITING_FILE'}
            </div>
          </div>
          <div className="panel-body">
            <FileUpload onFile={handleFile} disabled={uploading} />
            {uploadError && <div className="error-line">// ERROR: {uploadError}</div>}
          </div>
        </div>

        {/* DOCUMENT LIBRARY */}
        <div className="panel" style={{ marginTop: '24px' }}>
          <div className="panel-bar">
            <div className="panel-title">Library · Documents</div>
            <div className="panel-status">
              {docs === null ? 'LOADING' : `${docs.length} DOC(S)`}
            </div>
          </div>
          <div className="panel-body">
            {docs === null ? (
              <div className="gen-empty">// LOADING_DOCUMENTS…</div>
            ) : loadError ? (
              <div className="error-line">// ERROR: {loadError}</div>
            ) : docs.length === 0 ? (
              <div className="gen-empty">
                // NO_DOCUMENTS_YET — upload a .txt or .md file above to get started.
              </div>
            ) : (
              <div className="doc-grid">
                {docs.map((d) => (
                  <Link key={d.id} to={`/studio/${d.id}`} className="doc-card">
                    <span className="doc-card-icon">
                      <FileIcon size={20} />
                    </span>
                    <div className="doc-card-body">
                      <div className="doc-file">{d.fileName}</div>
                      <div className="doc-id">UPLOADED: {formatDate(d.uploadedAt)}</div>
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>

      <Footer />
    </>
  )
}
