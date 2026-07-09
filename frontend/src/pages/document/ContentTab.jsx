import { useEffect, useState } from 'react'
import { useParams, useOutletContext } from 'react-router-dom'
import { updateDocument } from '../../api/client.js'

// View + edit the document's title and raw text. The document (incl. content)
// is loaded once by the workspace (StudioDocument) and handed down via the
// Outlet context; on save we bubble the updated doc back so the header and
// other tabs see the new title/content.
export default function ContentTab() {
  const { docId } = useParams()
  const { doc, onDocUpdated } = useOutletContext()

  const [editing, setEditing] = useState(false)
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  // Sync the editable fields whenever we switch to a different document.
  useEffect(() => {
    if (doc) {
      setTitle(doc.fileName)
      setContent(doc.content ?? '')
    }
    setEditing(false)
    setError('')
  }, [doc?.id])

  if (!doc) {
    return (
      <div className="panel" style={{ marginTop: '8px' }}>
        <div className="panel-body">
          <div className="gen-empty">// LOADING_CONTENT…</div>
        </div>
      </div>
    )
  }

  async function handleSave() {
    const trimmed = title.trim()
    if (!trimmed) {
      setError('Title cannot be empty.')
      return
    }
    setSaving(true)
    setError('')
    try {
      const updated = await updateDocument(docId, { fileName: trimmed, content })
      onDocUpdated(updated)
      setEditing(false)
    } catch (err) {
      setError(err?.message || 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  function handleCancel() {
    setTitle(doc.fileName)
    setContent(doc.content ?? '')
    setEditing(false)
    setError('')
  }

  return (
    <div>
      <div className="studio-actions">
        {editing ? (
          <>
            <button className="btn primary" onClick={handleSave} disabled={saving}>
              {saving ? 'Saving…' : 'Save changes'}
            </button>
            <button className="btn ghost" onClick={handleCancel} disabled={saving}>
              Cancel
            </button>
          </>
        ) : (
          <button className="btn primary" onClick={() => setEditing(true)}>
            Edit content
          </button>
        )}
      </div>

      <div className="panel" style={{ marginTop: '8px' }}>
        <div className="panel-bar">
          <div className="panel-title">Content</div>
          <div className="panel-status">{editing ? 'EDITING' : 'READ_ONLY'}</div>
        </div>
        <div className="panel-body">
          {editing ? (
            <>
              <div className="doc-edit-note">
                // NOTE: editing content won't update flashcards or quizzes already
                generated from this document — regenerate them to match.
              </div>
              <label className="doc-edit-label" htmlFor="doc-title">
                Title
              </label>
              <input
                id="doc-title"
                className="doc-title-input"
                value={title}
                disabled={saving}
                onChange={(e) => setTitle(e.target.value)}
                aria-label="Document title"
              />
              <label className="doc-edit-label" htmlFor="doc-content">
                Content
              </label>
              <textarea
                id="doc-content"
                className="doc-content-input"
                value={content}
                disabled={saving}
                onChange={(e) => setContent(e.target.value)}
                spellCheck={false}
                aria-label="Document content"
              />
              {error && <div className="error-line">// ERROR: {error}</div>}
            </>
          ) : doc.content ? (
            <pre className="doc-content-view">{doc.content}</pre>
          ) : (
            <div className="gen-empty">// EMPTY_DOCUMENT — click Edit content to add text.</div>
          )}
        </div>
      </div>
    </div>
  )
}
