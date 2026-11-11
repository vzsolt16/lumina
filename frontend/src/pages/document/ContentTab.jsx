import { useEffect, useMemo, useRef, useState } from 'react'
import { useParams, useOutletContext } from 'react-router-dom'
import { EditorContent, useEditor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Underline from '@tiptap/extension-underline'
import Blockquote from '@tiptap/extension-blockquote'
import BulletList from '@tiptap/extension-bullet-list'
import CodeBlock from '@tiptap/extension-code-block'
import Heading from '@tiptap/extension-heading'
import HorizontalRule from '@tiptap/extension-horizontal-rule'
import Image from '@tiptap/extension-image'
import ListItem from '@tiptap/extension-list-item'
import OrderedList from '@tiptap/extension-ordered-list'
import Paragraph from '@tiptap/extension-paragraph'
import { Table } from '@tiptap/extension-table'
import TableCell from '@tiptap/extension-table-cell'
import TableHeader from '@tiptap/extension-table-header'
import TableRow from '@tiptap/extension-table-row'
import { updateDocument } from '../../api/client.js'
import { getFileExtension, getFileStem } from '../../constants/documentFormats.js'

const ID_ATTRIBUTE = {
  id: {
    default: null,
    parseHTML: (element) => element.getAttribute('data-node-id'),
    renderHTML: (attributes) =>
      attributes.id ? { 'data-node-id': attributes.id } : {},
  },
}

function withNodeId(extension) {
  return extension.extend({
    addAttributes() {
      return {
        ...(this.parent?.() ?? {}),
        ...ID_ATTRIBUTE,
      }
    },
  })
}

function createEditorExtensions() {
  const configured = StarterKit.configure({
    blockquote: false,
    bulletList: false,
    codeBlock: false,
    heading: false,
    horizontalRule: false,
    image: false,
    listItem: false,
    orderedList: false,
    paragraph: false,
    table: false,
    tableCell: false,
    tableHeader: false,
    tableRow: false,
  })

  return [
    configured,
    Underline,
    withNodeId(Blockquote),
    withNodeId(BulletList),
    withNodeId(CodeBlock),
    withNodeId(Heading),
    withNodeId(HorizontalRule),
    withNodeId(Image),
    withNodeId(ListItem),
    withNodeId(OrderedList),
    withNodeId(Paragraph),
    withNodeId(Table),
    withNodeId(TableCell),
    withNodeId(TableHeader),
    withNodeId(TableRow),
  ]
}

// The backend assigns ids to missing block nodes on save. Declaring the attr on
// every block extension keeps existing ids in Tiptap JSON and DOM parsing.
const extensions = createEditorExtensions()

function parseContent(doc) {
  if (doc?.contentJson) {
    try {
      const parsed = JSON.parse(doc.contentJson)
      if (parsed?.type === 'doc') return parsed
    } catch {
      // The backend backfills legacy documents; this is only a defensive
      // fallback for an incomplete response.
    }
  }

  const lines = String(doc?.content ?? '').replace(/\r\n?/g, '\n').split('\n')
  return {
    type: 'doc',
    content: lines.map((text) => ({
      type: 'paragraph',
      content: text ? [{ type: 'text', text }] : undefined,
    })),
  }
}

function Toolbar({ editor }) {
  if (!editor) return null

  const button = (label, action, active = false, disabled = false) => (
    <button
      type="button"
      className={`editor-tool${active ? ' active' : ''}`}
      onClick={action}
      disabled={disabled}
      aria-pressed={active}
      title={label}
    >
      {label}
    </button>
  )

  return (
    <div className="editor-toolbar" role="toolbar" aria-label="Formatting">
      {button('B', () => editor.chain().focus().toggleBold().run(), editor.isActive('bold'))}
      {button('I', () => editor.chain().focus().toggleItalic().run(), editor.isActive('italic'))}
      {button('U', () => editor.chain().focus().toggleUnderline().run(), editor.isActive('underline'))}
      {button('S', () => editor.chain().focus().toggleStrike().run(), editor.isActive('strike'))}
      <span className="editor-tool-separator" />
      {button('H1', () => editor.chain().focus().toggleHeading({ level: 1 }).run(), editor.isActive('heading', { level: 1 }))}
      {button('H2', () => editor.chain().focus().toggleHeading({ level: 2 }).run(), editor.isActive('heading', { level: 2 }))}
      {button('H3', () => editor.chain().focus().toggleHeading({ level: 3 }).run(), editor.isActive('heading', { level: 3 }))}
      {button('• List', () => editor.chain().focus().toggleBulletList().run(), editor.isActive('bulletList'))}
      {button('1. List', () => editor.chain().focus().toggleOrderedList().run(), editor.isActive('orderedList'))}
      {button('Quote', () => editor.chain().focus().toggleBlockquote().run(), editor.isActive('blockquote'))}
      {button('Rule', () => editor.chain().focus().setHorizontalRule().run())}
      {button('↵', () => editor.chain().focus().setHardBreak().run(), false, !editor.can().setHardBreak())}
      <span className="editor-tool-spacer" />
      {button('Undo', () => editor.chain().focus().undo().run(), false, !editor.can().undo())}
      {button('Redo', () => editor.chain().focus().redo().run(), false, !editor.can().redo())}
    </div>
  )
}

export default function ContentTab() {
  const { docId } = useParams()
  const { doc, onDocUpdated, onContentDirtyChange } = useOutletContext()
  const [editing, setEditing] = useState(false)
  const [title, setTitle] = useState('')
  const [contentView, setContentView] = useState('formatted')
  const [dirty, setDirty] = useState(false)
  const [saveState, setSaveState] = useState('saved')
  const [error, setError] = useState('')
  const versionRef = useRef(0)
  const pendingRef = useRef(null)
  const saveLoopRef = useRef(false)
  const latestDocIdRef = useRef(null)
  const externalVersionRef = useRef(null)

  const initialContent = useMemo(() => parseContent(doc), [doc?.id])
  const editor = useEditor({
    extensions,
    content: initialContent,
    editable: editing,
    onUpdate: () => {
      setDirty(true)
      setSaveState('unsaved')
      setError('')
    },
  })

  useEffect(() => {
    if (!doc || !editor || latestDocIdRef.current === doc.id) return
    latestDocIdRef.current = doc.id
    versionRef.current = doc.contentVersion ?? 1
    externalVersionRef.current = doc.contentVersion ?? 1
    setTitle(getFileStem(doc.fileName))
    setEditing(false)
    setDirty(false)
    setSaveState('saved')
    setError('')
    editor.setEditable(false)
    editor.commands.setContent(parseContent(doc), false)
  }, [doc?.id, editor])

  useEffect(() => {
    if (!doc || !editor || doc.id !== latestDocIdRef.current) return
    const incomingVersion = doc.contentVersion ?? 1
    if (incomingVersion === externalVersionRef.current) return
    externalVersionRef.current = incomingVersion
    versionRef.current = incomingVersion
    if (dirty) {
      setSaveState('error')
      setError('The document changed elsewhere. Save or reload before continuing.')
      return
    }
    editor.commands.setContent(parseContent(doc), false)
    setSaveState('saved')
  }, [doc, editor, dirty])

  useEffect(() => {
    editor?.setEditable(editing)
  }, [editor, editing])

  useEffect(() => {
    if (!editing || !dirty || !editor) return undefined
    const timer = window.setTimeout(() => {
      pendingRef.current = {
        contentJson: editor.getJSON(),
        fileName: `${title.trim()}${getFileExtension(doc.fileName)}`,
      }
      if (saveLoopRef.current) return

      saveLoopRef.current = true
      const run = async () => {
        while (pendingRef.current) {
          const next = pendingRef.current
          pendingRef.current = null
          setSaveState('saving')
          try {
            const updated = await updateDocument(docId, {
              fileName: next.fileName,
              contentJson: JSON.stringify(next.contentJson),
              expectedContentVersion: versionRef.current,
            })
            versionRef.current = updated.contentVersion ?? versionRef.current
            externalVersionRef.current = versionRef.current
            onDocUpdated(updated)
            setDirty(false)
            setSaveState('saved')
          } catch (err) {
            setSaveState('error')
            setError(
              err?.status === 409
                ? 'This document changed elsewhere. Reload it before saving again.'
                : err?.message || 'Autosave failed.',
            )
            pendingRef.current = null
            break
          }
        }
        saveLoopRef.current = false
      }
      run()
    }, 800)
    return () => window.clearTimeout(timer)
  }, [doc, docId, dirty, editing, editor, title, onDocUpdated])

  useEffect(() => {
    // Only an actively edited document can have changes that need guarding.
    // Tiptap may emit lifecycle updates while the read-only editor is being
    // initialized, and those must not count as unsaved user input.
    onContentDirtyChange?.(editing && dirty)
    return () => onContentDirtyChange?.(false)
  }, [dirty, editing, onContentDirtyChange])

  if (!doc) {
    return (
      <div className="panel" style={{ marginTop: '8px' }}>
        <div className="panel-body"><div className="gen-empty">// LOADING_CONTENT…</div></div>
      </div>
    )
  }

  function startEditing() {
    setEditing(true)
    setSaveState(dirty ? 'unsaved' : 'saved')
  }

  function cancelEditing() {
    editor?.commands.setContent(parseContent(doc), false)
    setTitle(getFileStem(doc.fileName))
    setDirty(false)
    setEditing(false)
    setSaveState('saved')
    setError('')
  }

  const isMarkdown = getFileExtension(doc.fileName).toLowerCase() === '.md'
  const statusLabel = saveState === 'saving'
    ? 'SAVING…'
    : saveState === 'unsaved'
      ? 'UNSAVED'
      : saveState === 'error'
        ? 'SAVE_ERROR'
        : 'SAVED'

  return (
    <div>
      <div className="studio-actions">
        {editing ? (
          <button className="btn ghost" onClick={cancelEditing} disabled={saveState === 'saving'}>
            Discard changes
          </button>
        ) : (
          <button className="btn primary" onClick={startEditing}>Edit content</button>
        )}
      </div>

      <div className="panel" style={{ marginTop: '8px' }}>
        <div className="panel-bar">
          <div className="panel-title">Content</div>
          {editing ? (
            <div className={`panel-status${saveState === 'saved' ? ' live' : ''}`}>
              {statusLabel}
            </div>
          ) : isMarkdown ? (
            <div className="doc-view-toggle" role="group" aria-label="Content view">
              <button type="button" className={`doc-view-toggle-btn${contentView === 'formatted' ? ' active' : ''}`} onClick={() => setContentView('formatted')}>Formatted</button>
              <button type="button" className={`doc-view-toggle-btn${contentView === 'raw' ? ' active' : ''}`} onClick={() => setContentView('raw')}>Raw</button>
            </div>
          ) : <div className="panel-status">READ_ONLY</div>}
        </div>
        <div className="panel-body">
          {editing ? (
            <>
              <div className="doc-edit-note">
                // NOTE: editing content won't update flashcards or quizzes already generated from this document — regenerate them to match.
              </div>
              <label className="doc-edit-label" htmlFor="doc-title">File name</label>
              <div className="doc-name-input">
                <input id="doc-title" className="doc-title-input" value={title} onChange={(e) => { setTitle(e.target.value); setDirty(true); setSaveState('unsaved') }} aria-label="Document name without extension" />
                <span className="doc-extension" aria-hidden="true">{getFileExtension(doc.fileName)}</span>
              </div>
              <label className="doc-edit-label">Content</label>
              <Toolbar editor={editor} />
              <EditorContent editor={editor} className="doc-editor" />
              {error && <div className="error-line">// ERROR: {error}</div>}
            </>
          ) : doc.content ? (
            isMarkdown && contentView === 'raw' ? (
              <pre className="doc-content-view">{doc.content}</pre>
            ) : (
              <EditorContent editor={editor} className="doc-editor doc-editor-readonly" />
            )
          ) : <div className="gen-empty">// EMPTY_DOCUMENT — click Edit content to add text.</div>}
        </div>
      </div>
    </div>
  )
}
