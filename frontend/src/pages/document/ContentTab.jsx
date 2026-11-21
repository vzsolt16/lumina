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
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { downloadCurrentDocument, downloadOriginalDocument, updateDocument } from '../../api/client.js'
import { uploadDocumentAsset } from '../../api/client.js'
import { AssetImage } from './AssetImageNode.jsx'
import { getDocumentRepresentation, getFileExtension, getFileStem } from '../../constants/documentFormats.js'

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
    withNodeId(AssetImage(Image)),
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

function preserveLineEndingStyle(original, edited) {
  if (!original.includes('\r')) return edited
  const crlf = (original.match(/\r\n/g) || []).length
  const withoutCrlf = original.replace(/\r\n/g, '')
  const loneLf = (withoutCrlf.match(/\n/g) || []).length
  const loneCr = (withoutCrlf.match(/\r/g) || []).length
  const newline = crlf >= loneLf && crlf >= loneCr ? '\r\n' : loneCr > loneLf ? '\r' : '\n'
  return edited.replace(/\r\n|\r|\n/g, '\n').replace(/\n/g, newline)
}

function parseContent(doc) {
  if (doc?.contentJson) {
    try {
      const parsed = JSON.parse(doc.contentJson)
      if (parsed?.type === 'doc') {
        const attachDocumentId = (node) => {
          if (!node || typeof node !== 'object') return
          if (node.type === 'image') {
            node.attrs = { ...(node.attrs || {}), documentId: doc.id }
          }
          node.content?.forEach(attachDocumentId)
        }
        attachDocumentId(parsed)
        return parsed
      }
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

function Toolbar({ editor, onUpload }) {
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
      {button('Image', onUpload)}
      {editor.isActive('image') && (
        <>
          <span className="editor-tool-separator" />
          {button('Left', () => editor.chain().focus().updateAttributes('image', { align: 'left' }).run())}
          {button('Center', () => editor.chain().focus().updateAttributes('image', { align: 'center' }).run())}
          {button('Right', () => editor.chain().focus().updateAttributes('image', { align: 'right' }).run())}
          {button('Resize', () => {
            const width = Number(window.prompt('Image width in pixels', editor.getAttributes('image').width || 600))
            if (Number.isFinite(width) && width > 0) {
              editor.chain().focus().updateAttributes('image', { width }).run()
            }
          })}
        </>
      )}
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
  const [sourceText, setSourceText] = useState('')
  const [contentView, setContentView] = useState('formatted')
  const [dirty, setDirty] = useState(false)
  const [saveState, setSaveState] = useState('saved')
  const [error, setError] = useState('')
  const fileInputRef = useRef(null)
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
    setSourceText(doc.content ?? '')
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
    setSourceText(doc.content ?? '')
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
        ...(getDocumentRepresentation(doc.fileName, doc.capabilities) === 'sourceText'
          ? { content: preserveLineEndingStyle(doc.content ?? '', sourceText) }
          : { contentJson: editor.getJSON() }),
        fileName: doc.fileName.toLowerCase() === 'dockerfile' && title.trim().toLowerCase() === 'dockerfile'
          ? doc.fileName
          : `${title.trim()}${getFileExtension(doc.fileName)}`,
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
              ...(next.fileName !== doc.fileName ? { fileName: next.fileName } : {}),
              ...(next.content !== undefined ? { content: next.content } : { contentJson: JSON.stringify(next.contentJson) }),
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
  }, [doc, docId, dirty, editing, editor, title, sourceText, onDocUpdated])

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
    setSourceText(doc?.content ?? '')
    editor?.commands.setContent(parseContent(doc), false)
    setTitle(getFileStem(doc.fileName))
    setDirty(false)
    setEditing(false)
    setSaveState('saved')
    setError('')
  }

  async function insertImage(file) {
    try {
      const asset = await uploadDocumentAsset(docId, file)
      editor?.chain().focus().setImage({
        assetId: asset.id,
        documentId: docId,
        alt: asset.fileName,
        width: asset.width,
        height: asset.height,
      }).run()
      setDirty(true)
      setSaveState('unsaved')
    } catch (err) {
      setError(err?.message || 'Image upload failed.')
    }
  }

  const representation = getDocumentRepresentation(doc.fileName, doc.capabilities)
  const isSourceText = representation === 'sourceText'
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
        <button className="btn ghost" onClick={async () => {
          try {
            const { blob, fileName } = await downloadCurrentDocument(doc.id)
            const url = URL.createObjectURL(blob)
            const link = document.createElement('a')
            link.href = url
            link.download = fileName
            link.click()
            URL.revokeObjectURL(url)
          } catch (err) { setError(err?.message || 'Download failed.') }
        }}>Download current content</button>
        <button className="btn ghost" onClick={async () => {
          try {
            const { blob, fileName } = await downloadOriginalDocument(doc.id)
            const url = URL.createObjectURL(blob)
            const link = document.createElement('a')
            link.href = url
            link.download = fileName
            link.click()
            URL.revokeObjectURL(url)
          } catch (err) { setError(err?.message || 'Download failed.') }
        }}>Download original</button>
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
          {doc.capabilities?.editsAffectExtractedContentOnly && !doc.capabilities?.legacyStructuredRepresentation && (
            <div className="doc-edit-note">// EXTRACTED CONTENT: changes apply to Lumina’s editable text only. The original {getFileExtension(doc.fileName).toUpperCase()} file remains unchanged; current content downloads as extracted text.</div>
          )}
          {doc.capabilities?.legacyStructuredRepresentation && (
            <div className="doc-edit-note">// LEGACY REPRESENTATION: this document was previously converted to rich text. Its original upload is preserved separately; editing this view does not restore source formatting.</div>
          )}
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
              {isSourceText ? (
                <textarea
                  className="doc-source-editor"
                  aria-label="Source content"
                  value={sourceText}
                  onChange={(event) => {
                    setSourceText(event.target.value)
                    setDirty(true)
                    setSaveState('unsaved')
                    setError('')
                  }}
                  spellCheck={false}
                />
              ) : (
                <>
                  <Toolbar editor={editor} onUpload={() => fileInputRef.current?.click()} />
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept="image/png,image/jpeg,image/gif,image/webp"
                    hidden
                    onChange={(event) => {
                      const file = event.target.files?.[0]
                      if (file) insertImage(file)
                      event.target.value = ''
                    }}
                  />
                  <EditorContent editor={editor} className="doc-editor" />
                </>
              )}
              {error && <div className="error-line">// ERROR: {error}</div>}
            </>
          ) : doc.content ? (
            isSourceText ? (
              isMarkdown && contentView === 'formatted' ? (
                <div className="doc-markdown-preview"><ReactMarkdown remarkPlugins={[remarkGfm]}>{doc.content}</ReactMarkdown></div>
              ) : <pre className="doc-content-view">{doc.content}</pre>
            ) : isMarkdown && contentView === 'raw' ? (
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
