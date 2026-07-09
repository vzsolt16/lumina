import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import Nav from '../components/Nav.jsx'
import Footer from '../components/Footer.jsx'
import FileUpload from '../components/FileUpload.jsx'
import {
  FileIcon,
  FolderIcon,
  MoreIcon,
  PencilIcon,
  PlusIcon,
  TrashIcon,
} from '../components/icons.jsx'
import {
  getDocuments,
  getFolders,
  uploadDocument,
  deleteDocument,
  updateDocument,
  moveDocument,
  createFolder,
  renameFolder,
  moveFolder,
  deleteFolder,
} from '../api/client.js'
import './Studio.css'

function formatDate(value) {
  if (!value) return ''
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString()
}

// Drag payload is serialized into dataTransfer as JSON: { kind: 'doc'|'folder', id }.
const DRAG_MIME = 'application/x-lumina-item'

// All folders descended from rootId, including rootId itself.
function collectSubtree(folders, rootId) {
  const ids = new Set()
  const stack = [rootId]
  while (stack.length) {
    const id = stack.pop()
    ids.add(id)
    for (const f of folders) if (f.parentId === id) stack.push(f.id)
  }
  return ids
}

// Is candidateId inside the subtree rooted at ancestorId? Walk up from candidate.
function isDescendant(folders, candidateId, ancestorId) {
  const byId = new Map(folders.map((f) => [f.id, f]))
  let cur = byId.get(candidateId)
  while (cur && cur.parentId != null) {
    if (cur.parentId === ancestorId) return true
    cur = byId.get(cur.parentId)
  }
  return false
}

// ── BREADCRUMBS ────────────────────────────────────────────────────────
// Each crumb is a drop target too — drop a card onto an ancestor to move it there.
function Breadcrumbs({ trail, onNavigate, dragItem, folders, onDropOnFolder }) {
  return (
    <nav className="crumbs" aria-label="Folder path">
      {trail.map((crumb, i) => {
        const isLast = i === trail.length - 1
        const canDrop = dragItem && canDropInto(dragItem, crumb.id, folders)
        return (
          <span key={crumb.id ?? 'root'} className="crumb-wrap">
            {i > 0 && <span className="crumb-sep">/</span>}
            <button
              type="button"
              className={`crumb${isLast ? ' current' : ''}`}
              onClick={() => onNavigate(crumb.id)}
              aria-current={isLast ? 'page' : undefined}
              onDragOver={(e) => {
                if (canDrop) {
                  e.preventDefault()
                  e.dataTransfer.dropEffect = 'move'
                }
              }}
              onDrop={(e) => {
                if (!canDrop) return
                e.preventDefault()
                onDropOnFolder(crumb.id, e)
              }}
              data-droppable={canDrop ? 'true' : undefined}
            >
              {crumb.name}
            </button>
          </span>
        )
      })}
    </nav>
  )
}

// Shared drop rule used by both folder cards and breadcrumbs.
// targetId is the folder being dropped INTO (null = root).
function canDropInto(dragItem, targetId, folders) {
  if (!dragItem) return false
  if (dragItem.kind === 'doc') {
    return dragItem.parentId !== targetId
  }
  // folder
  if (dragItem.id === targetId) return false
  if (dragItem.parentId === targetId) return false // no-op
  if (targetId != null && folders && isDescendant(folders, targetId, dragItem.id)) {
    return false // would move a parent into its own child
  }
  return true
}

// ── FOLDER CARD ────────────────────────────────────────────────────────
function FolderCard({
  folder,
  hasContents,
  dragItem,
  folders,
  onOpen,
  onDragStart,
  onDragEnd,
  onDropInto,
  onRename,
  onDelete,
}) {
  const [menuOpen, setMenuOpen] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(folder.name)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [dragOver, setDragOver] = useState(false)
  const wrapRef = useRef(null)

  useEffect(() => {
    if (!menuOpen) return
    function onDocClick(e) {
      if (wrapRef.current && !wrapRef.current.contains(e.target)) {
        setMenuOpen(false)
        setConfirming(false)
      }
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [menuOpen])

  const canDrop = canDropInto(dragItem, folder.id, folders)

  async function submitRename() {
    const trimmed = name.trim()
    if (!trimmed || trimmed === folder.name) {
      setEditing(false)
      setName(folder.name)
      return
    }
    setBusy(true)
    setError('')
    try {
      await onRename(folder.id, trimmed)
      setEditing(false)
      setMenuOpen(false)
    } catch (err) {
      setError(err?.message || 'Rename failed.')
      setName(folder.name)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    setBusy(true)
    setError('')
    try {
      await onDelete(folder.id)
    } catch (err) {
      setError(err?.message || 'Delete failed.')
      setBusy(false)
      setConfirming(false)
    }
  }

  return (
    <div
      className={`doc-card-wrap${dragOver && canDrop ? ' drop-target' : ''}`}
      ref={wrapRef}
      draggable={!editing}
      onDragStart={(e) => {
        if (editing) return
        onDragStart(e, { kind: 'folder', id: folder.id, parentId: folder.parentId })
      }}
      onDragEnd={onDragEnd}
      onDragOver={(e) => {
        if (canDrop) {
          e.preventDefault()
          e.dataTransfer.dropEffect = 'move'
          setDragOver(true)
        }
      }}
      onDragLeave={() => setDragOver(false)}
      onDrop={(e) => {
        setDragOver(false)
        if (!canDrop) return
        e.preventDefault()
        onDropInto(folder.id, e)
      }}
    >
      {editing ? (
        <div className="folder-card folder-card-edit">
          <span className="doc-card-icon">
            <FolderIcon size={20} />
          </span>
          <input
            className="folder-rename-input"
            value={name}
            autoFocus
            disabled={busy}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') submitRename()
              if (e.key === 'Escape') {
                setEditing(false)
                setName(folder.name)
              }
            }}
            onBlur={submitRename}
            aria-label="Folder name"
          />
        </div>
      ) : (
        <button
          type="button"
          className="folder-card"
          onClick={() => onOpen(folder.id)}
          onDoubleClick={() => onOpen(folder.id)}
        >
          <span className="doc-card-icon">
            <FolderIcon size={20} />
          </span>
          <div className="doc-card-body">
            <div className="doc-file">{folder.name}</div>
            <div className="doc-id">FOLDER</div>
          </div>
        </button>
      )}

      <button
        type="button"
        className="doc-menu-btn"
        aria-label="Folder options"
        aria-haspopup="true"
        aria-expanded={menuOpen}
        onClick={() => {
          setMenuOpen((v) => !v)
          setConfirming(false)
          setError('')
        }}
      >
        <MoreIcon size={18} />
      </button>

      {menuOpen && (
        <div className="doc-menu" role="menu">
          {confirming ? (
            <div className="doc-menu-confirm">
              <span className="doc-menu-confirm-q">
                // DELETE_FOLDER + ALL CONTENTS?
              </span>
              <div className="doc-menu-confirm-actions">
                <button
                  type="button"
                  className="doc-menu-item"
                  onClick={() => setConfirming(false)}
                  disabled={busy}
                >
                  CANCEL
                </button>
                <button
                  type="button"
                  className="doc-menu-item danger"
                  onClick={handleDelete}
                  disabled={busy}
                >
                  {busy ? 'DELETING…' : 'CONFIRM'}
                </button>
              </div>
            </div>
          ) : (
            <>
              <button
                type="button"
                className="doc-menu-item"
                role="menuitem"
                onClick={() => {
                  setEditing(true)
                  setMenuOpen(false)
                }}
              >
                <PencilIcon size={15} />
                <span>Rename</span>
              </button>
              <button
                type="button"
                className="doc-menu-item danger"
                role="menuitem"
                onClick={() => {
                  // Empty folders delete instantly; non-empty ask first.
                  if (hasContents) setConfirming(true)
                  else handleDelete()
                }}
              >
                <TrashIcon size={15} />
                <span>Delete</span>
              </button>
            </>
          )}
          {error && <div className="error-line">// ERROR: {error}</div>}
        </div>
      )}
    </div>
  )
}

// ── DOCUMENT CARD ──────────────────────────────────────────────────────
function DocCard({ doc, onDeleted, onRename, onDragStart, onDragEnd }) {
  const [menuOpen, setMenuOpen] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(doc.fileName)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const wrapRef = useRef(null)

  useEffect(() => {
    if (!menuOpen) return
    function onDocClick(e) {
      if (wrapRef.current && !wrapRef.current.contains(e.target)) {
        setMenuOpen(false)
        setConfirming(false)
      }
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [menuOpen])

  async function submitRename() {
    const trimmed = name.trim()
    if (!trimmed || trimmed === doc.fileName) {
      setEditing(false)
      setName(doc.fileName)
      return
    }
    setBusy(true)
    setError('')
    try {
      await onRename(doc.id, trimmed)
      setEditing(false)
    } catch (err) {
      setError(err?.message || 'Rename failed.')
      setName(doc.fileName)
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    setDeleting(true)
    setError('')
    try {
      await deleteDocument(doc.id)
      onDeleted(doc.id)
    } catch (err) {
      setError(err?.message || 'Delete failed.')
      setDeleting(false)
      setConfirming(false)
    }
  }

  return (
    <div
      className="doc-card-wrap"
      ref={wrapRef}
      draggable={!editing}
      onDragStart={(e) => {
        if (editing) return
        onDragStart(e, { kind: 'doc', id: doc.id, parentId: doc.folderId ?? null })
      }}
      onDragEnd={onDragEnd}
    >
      {editing ? (
        <div className="doc-card doc-card-edit">
          <span className="doc-card-icon">
            <FileIcon size={20} />
          </span>
          <input
            className="folder-rename-input"
            value={name}
            autoFocus
            disabled={busy}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') submitRename()
              if (e.key === 'Escape') {
                setEditing(false)
                setName(doc.fileName)
              }
            }}
            onBlur={submitRename}
            aria-label="Document title"
          />
        </div>
      ) : (
        <Link to={`/studio/${doc.id}`} className="doc-card" draggable={false}>
          <span className="doc-card-icon">
            <FileIcon size={20} />
          </span>
          <div className="doc-card-body">
            <div className="doc-file">{doc.fileName}</div>
            <div className="doc-id">UPLOADED: {formatDate(doc.uploadedAt)}</div>
          </div>
        </Link>
      )}

      <button
        type="button"
        className="doc-menu-btn"
        aria-label="Document options"
        aria-haspopup="true"
        aria-expanded={menuOpen}
        onClick={() => {
          setMenuOpen((v) => !v)
          setConfirming(false)
          setError('')
        }}
      >
        <MoreIcon size={18} />
      </button>

      {menuOpen && (
        <div className="doc-menu" role="menu">
          {confirming ? (
            <div className="doc-menu-confirm">
              <span className="doc-menu-confirm-q">// DELETE_DOCUMENT?</span>
              <div className="doc-menu-confirm-actions">
                <button
                  type="button"
                  className="doc-menu-item"
                  onClick={() => setConfirming(false)}
                  disabled={deleting}
                >
                  CANCEL
                </button>
                <button
                  type="button"
                  className="doc-menu-item danger"
                  onClick={handleDelete}
                  disabled={deleting}
                >
                  {deleting ? 'DELETING…' : 'CONFIRM'}
                </button>
              </div>
            </div>
          ) : (
            <>
              <button
                type="button"
                className="doc-menu-item"
                role="menuitem"
                onClick={() => {
                  setName(doc.fileName)
                  setEditing(true)
                  setMenuOpen(false)
                }}
              >
                <PencilIcon size={15} />
                <span>Rename</span>
              </button>
              <button
                type="button"
                className="doc-menu-item danger"
                role="menuitem"
                onClick={() => setConfirming(true)}
              >
                <TrashIcon size={15} />
                <span>Delete</span>
              </button>
            </>
          )}
          {error && <div className="error-line">// ERROR: {error}</div>}
        </div>
      )}
    </div>
  )
}

export default function Studio() {
  const navigate = useNavigate()
  const [docs, setDocs] = useState(null) // null = loading
  const [folders, setFolders] = useState(null)
  const [currentFolderId, setCurrentFolderId] = useState(null)
  const [loadError, setLoadError] = useState('')
  const [actionError, setActionError] = useState('')
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState('')

  // New-folder inline control.
  const [creating, setCreating] = useState(false)
  const [newName, setNewName] = useState('')
  const [createBusy, setCreateBusy] = useState(false)

  // The item currently being dragged (drives drop-target highlighting).
  const [dragItem, setDragItem] = useState(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([getFolders(), getDocuments()])
      .then(([folderList, docList]) => {
        if (cancelled) return
        setFolders(Array.isArray(folderList) ? folderList : [])
        setDocs(Array.isArray(docList) ? docList : [])
      })
      .catch((err) => {
        if (cancelled) return
        setFolders([])
        setDocs([])
        setLoadError(err?.message || 'Could not load your library.')
      })
    return () => {
      cancelled = true
    }
  }, [])

  // Folder no longer exists (e.g. an ancestor was deleted) → snap back to root.
  useEffect(() => {
    if (folders && currentFolderId != null && !folders.some((f) => f.id === currentFolderId)) {
      setCurrentFolderId(null)
    }
  }, [folders, currentFolderId])

  const childFolders = useMemo(() => {
    if (!folders) return []
    return folders
      .filter((f) => f.parentId === currentFolderId)
      .sort((a, b) => a.name.localeCompare(b.name))
  }, [folders, currentFolderId])

  const folderDocs = useMemo(() => {
    if (!docs) return []
    return docs.filter((d) => (d.folderId ?? null) === currentFolderId)
  }, [docs, currentFolderId])

  const trail = useMemo(() => {
    const crumbs = [{ id: null, name: 'Library' }]
    if (!folders || currentFolderId == null) return crumbs
    const byId = new Map(folders.map((f) => [f.id, f]))
    const chain = []
    let cur = byId.get(currentFolderId)
    while (cur) {
      chain.unshift({ id: cur.id, name: cur.name })
      cur = cur.parentId != null ? byId.get(cur.parentId) : null
    }
    return crumbs.concat(chain)
  }, [folders, currentFolderId])

  function navigateTo(folderId) {
    setCurrentFolderId(folderId)
    setActionError('')
    setCreating(false)
  }

  async function handleFile(file) {
    setUploading(true)
    setUploadError('')
    try {
      const result = await uploadDocument(file, currentFolderId)
      navigate(`/studio/${result.id}`)
    } catch (err) {
      setUploadError(err?.message || 'Upload failed.')
      setUploading(false)
    }
  }

  async function handleCreateFolder() {
    const name = newName.trim()
    if (!name) {
      setCreating(false)
      return
    }
    setCreateBusy(true)
    setActionError('')
    try {
      const folder = await createFolder(name, currentFolderId)
      setFolders((prev) => [...prev, folder])
      setNewName('')
      setCreating(false)
    } catch (err) {
      setActionError(err?.message || 'Could not create folder.')
    } finally {
      setCreateBusy(false)
    }
  }

  async function handleRenameFolder(id, name) {
    const updated = await renameFolder(id, name)
    setFolders((prev) => prev.map((f) => (f.id === id ? updated : f)))
  }

  async function handleRenameDoc(id, fileName) {
    const updated = await updateDocument(id, { fileName })
    setDocs((prev) =>
      prev.map((d) =>
        d.id === id ? { ...d, fileName: updated.fileName, updatedAt: updated.updatedAt } : d,
      ),
    )
  }

  async function handleDeleteFolder(id) {
    await deleteFolder(id)
    // Server cascade-deletes the subtree + its documents; mirror that locally.
    const removed = collectSubtree(folders, id)
    setFolders((prev) => prev.filter((f) => !removed.has(f.id)))
    setDocs((prev) => prev.filter((d) => !removed.has(d.folderId)))
  }

  // ── drag-and-drop ──────────────────────────────────────────────────
  function handleDragStart(e, item) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData(DRAG_MIME, JSON.stringify(item))
    setDragItem(item)
    setActionError('')
  }

  function handleDragEnd() {
    setDragItem(null)
  }

  function readDragItem(e) {
    try {
      const raw = e.dataTransfer.getData(DRAG_MIME)
      if (raw) return JSON.parse(raw)
    } catch {
      /* fall through */
    }
    return dragItem
  }

  // Drop a doc/folder INTO targetFolderId (null = root).
  async function handleDropInto(targetFolderId, e) {
    const item = readDragItem(e)
    setDragItem(null)
    if (!item) return

    try {
      if (item.kind === 'doc') {
        if ((item.parentId ?? null) === targetFolderId) return
        await moveDocument(item.id, targetFolderId)
        setDocs((prev) =>
          prev.map((d) => (d.id === item.id ? { ...d, folderId: targetFolderId } : d)),
        )
      } else {
        if (!canDropInto(item, targetFolderId, folders)) return
        const updated = await moveFolder(item.id, targetFolderId)
        setFolders((prev) => prev.map((f) => (f.id === item.id ? updated : f)))
      }
    } catch (err) {
      setActionError(err?.message || 'Move failed.')
    }
  }

  const loading = docs === null || folders === null
  const isEmpty = childFolders.length === 0 && folderDocs.length === 0

  return (
    <>
      <Nav showSectionLinks={false} />

      <div className="studio">
        <div className="studio-head">
          <div className="section-tag">Studio · Library</div>
          <div className="section-heading">
            Your<br />documents
          </div>
          <div className="section-sub">
            — Organize material into folders, or upload something new
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

        {/* LIBRARY */}
        <div className="panel" style={{ marginTop: '24px' }}>
          <div className="panel-bar">
            <div className="panel-title">Library · Documents</div>
            <div className="panel-status">
              {loading
                ? 'LOADING'
                : `${childFolders.length} FOLDER(S) · ${folderDocs.length} DOC(S)`}
            </div>
          </div>
          <div className="panel-body">
            {/* toolbar: breadcrumbs + new folder */}
            {!loading && !loadError && (
              <div className="lib-toolbar">
                <Breadcrumbs
                  trail={trail}
                  onNavigate={navigateTo}
                  dragItem={dragItem}
                  folders={folders}
                  onDropOnFolder={handleDropInto}
                />
                {creating ? (
                  <div className="folder-create">
                    <input
                      className="folder-rename-input"
                      placeholder="Folder name"
                      value={newName}
                      autoFocus
                      disabled={createBusy}
                      onChange={(e) => setNewName(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') handleCreateFolder()
                        if (e.key === 'Escape') {
                          setCreating(false)
                          setNewName('')
                        }
                      }}
                      onBlur={handleCreateFolder}
                      aria-label="New folder name"
                    />
                  </div>
                ) : (
                  <button
                    type="button"
                    className="lib-new-folder"
                    onClick={() => {
                      setCreating(true)
                      setActionError('')
                    }}
                  >
                    <PlusIcon size={14} />
                    <span>New folder</span>
                  </button>
                )}
              </div>
            )}

            {actionError && <div className="error-line">// ERROR: {actionError}</div>}

            {loading ? (
              <div className="gen-empty">// LOADING_LIBRARY…</div>
            ) : loadError ? (
              <div className="error-line">// ERROR: {loadError}</div>
            ) : isEmpty ? (
              <div className="gen-empty">
                {currentFolderId == null
                  ? '// NO_DOCUMENTS_YET — upload a .txt or .md file above, or create a folder.'
                  : '// EMPTY_FOLDER — drag documents here, upload, or add a subfolder.'}
              </div>
            ) : (
              <div className="doc-grid">
                {childFolders.map((f) => (
                  <FolderCard
                    key={f.id}
                    folder={f}
                    hasContents={
                      folders.some((c) => c.parentId === f.id) ||
                      docs.some((d) => d.folderId === f.id)
                    }
                    dragItem={dragItem}
                    folders={folders}
                    onOpen={navigateTo}
                    onDragStart={handleDragStart}
                    onDragEnd={handleDragEnd}
                    onDropInto={handleDropInto}
                    onRename={handleRenameFolder}
                    onDelete={handleDeleteFolder}
                  />
                ))}
                {folderDocs.map((d) => (
                  <DocCard
                    key={d.id}
                    doc={d}
                    onDeleted={(id) => setDocs((prev) => prev.filter((x) => x.id !== id))}
                    onRename={handleRenameDoc}
                    onDragStart={handleDragStart}
                    onDragEnd={handleDragEnd}
                  />
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
