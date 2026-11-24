import { useEffect, useMemo, useRef, useState } from 'react'
import { useParams, useOutletContext } from 'react-router-dom'
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import {
  getConversations,
  getAiModels,
  setConversationModel,
  createConversation,
  getConversationMessages,
  deleteConversation,
  applyEditProposal,
  rejectEditProposal,
  getDocumentAssetBlob,
} from '../../api/client.js'
import { getAccessToken } from '../../api/authToken.js'
import { PlusIcon, TrashIcon } from '../../components/icons.jsx'

// Chat is a direct request/response stream, not a background job, so unlike
// flashcards/quiz it doesn't live in JobsContext — the connection and message
// state are owned by this tab and torn down when it unmounts.
//
// A document has many conversations: the sidebar lists them, the user can start
// new ones, delete them, or continue old ones. The hub connection is opened once
// per document; each streamed answer is keyed by the active conversation id via
// connection.stream('StreamAnswer', conversationId, question). Conversations
// auto-title from their first question (mirrored optimistically here).

// Mirrors the backend's title derivation so the sidebar updates instantly on the
// first message, before the server-side title comes back.
function deriveTitle(question) {
  const line = question.replace(/[\r\n]+/g, ' ').trim()
  if (line.length <= 40) return line || 'New chat'
  return line.slice(0, 40).trimEnd() + '…'
}

function formatTime(iso) {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const date = d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  return `${date} ${time}`
}

function getTextProposalChanges(proposal) {
  if (proposal.changes?.length) return proposal.changes

  const preview = String(proposal.replacement || '')
  const matches = [...preview.matchAll(/(?:^|\n)Target:\n([\s\S]*?)\nReplacement:\n([\s\S]*?)(?=\n\nTarget:\n|$)/g)]
  if (matches.length) {
    return matches.map((match) => ({ target: match[1], replacement: match[2] }))
  }
  if (preview.includes('Target:') || preview.includes('Replacement:')) return []

  // Legacy proposals without the formatted source-text preview stored the
  // actual target and replacement directly in these fields.
  if (proposal.target && preview) {
    return [{ target: proposal.target, replacement: preview }]
  }
  return []
}

function findImageNodes(node, result = []) {
  if (!node || typeof node !== 'object') return result
  if (node.type === 'image') result.push(node)
  if (Array.isArray(node.content)) {
    node.content.forEach((child) => findImageNodes(child, result))
  }
  return result
}

function getDocumentImages(doc) {
  if (!doc?.contentJson) return []
  try {
    return findImageNodes(JSON.parse(doc.contentJson))
  } catch {
    return []
  }
}

function ProposalAssetPreview({ documentId, node, label }) {
  const [src, setSrc] = useState('')

  useEffect(() => {
    let active = true
    let objectUrl = ''
    const assetId = node?.attrs?.assetId
    if (!assetId) return undefined

    getDocumentAssetBlob(documentId, assetId)
      .then((blob) => {
        if (!active) return
        objectUrl = URL.createObjectURL(blob)
        setSrc(objectUrl)
      })
      .catch(() => setSrc(''))

    return () => {
      active = false
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [documentId, node?.attrs?.assetId])

  if (!node) return null

  return (
    <div className="chat-proposal-image">
      {src ? (
        <img
          src={src}
          alt={node.attrs?.alt || label}
          className="chat-proposal-image-preview"
        />
      ) : (
        <div className="chat-proposal-image-placeholder">IMAGE</div>
      )}
      <div className="chat-proposal-image-meta">
        <span>{label}</span>
        {node.attrs?.width && node.attrs?.height && (
          <span>{node.attrs.width} × {node.attrs.height}</span>
        )}
      </div>
    </div>
  )
}

export default function ChatTab() {
  const { docId } = useParams()
  // Applying an AI edit changes the document; bubble the updated doc up so the
  // Content tab and header see the new text without a refetch.
  const { doc, onDocUpdated } = useOutletContext()
  const imageNodes = useMemo(() => getDocumentImages(doc), [doc?.contentJson])

  const [conversations, setConversations] = useState([])
  const [activeId, setActiveId] = useState(null)
  const [messages, setMessages] = useState([]) // { role, content, proposal? }[]
  const [models, setModels] = useState([])
  const [defaultModelId, setDefaultModelId] = useState('')
  const [selectedModelId, setSelectedModelId] = useState('')

  const [loadingConvos, setLoadingConvos] = useState(true)
  const [loadingMessages, setLoadingMessages] = useState(false)
  const [status, setStatus] = useState('connecting') // connecting | ready | error
  const [streaming, setStreaming] = useState(false)
  const [input, setInput] = useState('')
  const [error, setError] = useState('')

  const [confirmingId, setConfirmingId] = useState(null)
  const [deletingId, setDeletingId] = useState(null)
  // messageId of the proposal being applied/rejected right now.
  const [proposalBusyId, setProposalBusyId] = useState(null)

  const connectionRef = useRef(null)
  const subscriptionRef = useRef(null)
  const logRef = useRef(null)
  // Bumped on every message load so a stale in-flight fetch (after switching
  // conversations or documents) can't clobber the current transcript.
  const loadSeqRef = useRef(0)

  async function loadMessages(conversationId) {
    const seq = ++loadSeqRef.current
    setLoadingMessages(true)
    try {
      const msgs = await getConversationMessages(docId, conversationId)
      if (loadSeqRef.current === seq) {
        setMessages(
          (msgs || []).map((m) => ({
            role: m.role,
            content: m.content,
            proposal: m.proposal || null,
          })),
        )
      }
    } catch {
      if (loadSeqRef.current === seq) setError('Could not load this conversation.')
    } finally {
      if (loadSeqRef.current === seq) setLoadingMessages(false)
    }
  }

  // Load the document's conversations + open the hub connection.
  useEffect(() => {
    let cancelled = false

    // Invalidate any in-flight message load from a previous document.
    loadSeqRef.current++
    setConversations([])
    setActiveId(null)
    setMessages([])
    setModels([])
    setDefaultModelId('')
    setSelectedModelId('')
    setConfirmingId(null)
    setLoadingConvos(true)
    setError('')
    setStatus('connecting')

    Promise.all([getConversations(docId), getAiModels()])
      .then(([list, modelData]) => {
        if (cancelled) return
        const convos = Array.isArray(list) ? list : []
        const availableModels = Array.isArray(modelData?.models) ? modelData.models : []
        const fallbackModelId = modelData?.defaultModel || availableModels[0]?.id || ''
        setModels(availableModels)
        setDefaultModelId(fallbackModelId)
        setConversations(convos)
        if (convos.length) {
          setActiveId(convos[0].id)
          setSelectedModelId(convos[0].modelId || fallbackModelId)
          loadMessages(convos[0].id)
        } else {
          setSelectedModelId(fallbackModelId)
        }
      })
      .catch(() => {
        if (!cancelled) setError('Could not load conversations.')
      })
      .finally(() => {
        if (!cancelled) setLoadingConvos(false)
      })

    const connection = new HubConnectionBuilder()
      .withUrl('/ws/chat', {
        skipNegotiation: true,
        transport: HttpTransportType.WebSockets,
        accessTokenFactory: () => getAccessToken(),
      })
      .withAutomaticReconnect()
      .build()

    connection.onreconnecting(() => !cancelled && setStatus('connecting'))
    connection.onreconnected(() => !cancelled && setStatus('ready'))

    connectionRef.current = connection
    const startPromise = connection.start()
    startPromise
      .then(() => {
        if (!cancelled) setStatus('ready')
      })
      .catch((err) => {
        if (!cancelled) {
          setStatus('error')
          setError(err?.message || 'Could not connect to chat.')
        }
      })

    return () => {
      cancelled = true
      subscriptionRef.current?.dispose()
      subscriptionRef.current = null
      startPromise
        .catch(() => {})
        .then(() => connection.stop())
        .catch(() => {})
      if (connectionRef.current === connection) connectionRef.current = null
    }
  }, [docId])

  // Keep the log pinned to the latest message as it grows.
  useEffect(() => {
    const el = logRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages])

  function selectConversation(id) {
    if (streaming || id === activeId) return
    setError('')
    setConfirmingId(null)
    setActiveId(id)
    const conversation = conversations.find((item) => item.id === id)
    setSelectedModelId(conversation?.modelId || defaultModelId)
    loadMessages(id)
  }

  async function changeModel(event) {
    const modelId = event.target.value
    setSelectedModelId(modelId)
    if (!activeId) return
    try {
      await setConversationModel(docId, activeId, modelId)
      setConversations((prev) => prev.map((conversation) =>
        conversation.id === activeId ? { ...conversation, modelId } : conversation,
      ))
    } catch (err) {
      setError(err?.message || 'Could not save the selected model.')
    }
  }

  async function newChat() {
    if (streaming) return
    setError('')
    setConfirmingId(null)
    try {
      const convo = await createConversation(docId)
      setConversations((prev) => [convo, ...prev])
      loadSeqRef.current++ // no fetch needed; a fresh conversation is empty
      setActiveId(convo.id)
      setSelectedModelId(convo.modelId || defaultModelId)
      setMessages([])
    } catch {
      setError('Could not start a new chat.')
    }
  }

  async function confirmDelete(id) {
    setDeletingId(id)
    setError('')
    try {
      await deleteConversation(docId, id)
      const remaining = conversations.filter((c) => c.id !== id)
      setConversations(remaining)
      setConfirmingId(null)
      if (id === activeId) {
        if (remaining.length) {
          setActiveId(remaining[0].id)
          setSelectedModelId(remaining[0].modelId || defaultModelId)
          loadMessages(remaining[0].id)
        } else {
          loadSeqRef.current++
          setActiveId(null)
          setMessages([])
        }
      }
    } catch {
      setError('Delete failed.')
    } finally {
      setDeletingId(null)
    }
  }

  async function send(e) {
    e.preventDefault()
    const question = input.trim()
    if (!question || streaming || status !== 'ready') return

    setError('')
    setInput('')
    setStreaming(true)

    // Lazily create a conversation the first time the user talks into an empty
    // document, so we never persist an empty "New chat" they never used.
    let convoId = activeId
    let isFirst = messages.length === 0
    if (!convoId) {
      try {
        const convo = await createConversation(docId)
        setConversations((prev) => [convo, ...prev])
        loadSeqRef.current++
        setActiveId(convo.id)
        convoId = convo.id
        isFirst = true
      } catch {
        setStreaming(false)
        setError('Could not start a new chat.')
        return
      }
    }

    // Optimistically show the question and an empty assistant slot we fill in
    // as tokens arrive. `draft` accumulates outside state to dodge stale closures.
    setMessages((prev) => [
      ...prev,
      { role: 'user', content: question },
      { role: 'assistant', content: '' },
    ])

    // Name the conversation from its opening question and float it to the top,
    // matching what the backend persists on completion.
    if (isFirst) {
      const title = deriveTitle(question)
      setConversations((prev) => {
        const next = prev.map((c) =>
          c.id === convoId ? { ...c, title, updatedAt: new Date().toISOString() } : c,
        )
        next.sort((a, b) => new Date(b.updatedAt) - new Date(a.updatedAt))
        return next
      })
    }

    let draft = ''
    // Merge instead of replace so a proposal already attached to the message
    // survives later updates.
    const patchAnswer = (patch) =>
      setMessages((prev) => {
        const next = prev.slice()
        next[next.length - 1] = { ...next[next.length - 1], ...patch }
        return next
      })

    // Track tool-call progress on the in-progress message: show the activity when
    // the tool starts, drop it when done. Tools like rename finish near-instantly,
    // so keep the line up for a minimum window — otherwise it just flickers.
    const MIN_ACTIVITY_MS = 1200
    let activitySeq = 0
    const runningStart = new Map() // toolName -> { id, startedAt }

    const addActivity = (id, label) =>
      setMessages((prev) => {
        const next = prev.slice()
        const last = next[next.length - 1]
        const activities = last.activities ? [...last.activities, { id, label }] : [{ id, label }]
        next[next.length - 1] = { ...last, activities }
        return next
      })

    const removeActivity = (id) =>
      setMessages((prev) =>
        prev.map((m) =>
          m.activities?.some((a) => a.id === id)
            ? { ...m, activities: m.activities.filter((a) => a.id !== id) }
            : m,
        ),
      )

    const upsertActivity = (name, label, status) => {
      if (status === 'running') {
        const id = ++activitySeq
        runningStart.set(name, { id, startedAt: Date.now() })
        addActivity(id, label)
      } else {
        const entry = runningStart.get(name)
        if (!entry) return
        runningStart.delete(name)
        const wait = Math.max(0, MIN_ACTIVITY_MS - (Date.now() - entry.startedAt))
        if (wait === 0) removeActivity(entry.id)
        else setTimeout(() => removeActivity(entry.id), wait)
      }
    }

    const connection = connectionRef.current
    if (!connection || connection.state !== 'Connected') {
      setStreaming(false)
      setError('Chat is still connecting. Try again in a moment.')
      setMessages((prev) => prev.slice(0, -2))
      return
    }

    subscriptionRef.current = connection
      .stream('StreamAnswer', convoId, question, selectedModelId || null)
      .subscribe({
        next: (evt) => {
          // The stream carries typed events: token fragments of the reply, an
          // optional trailing edit proposal, and a document update when a tool
          // (e.g. rename) changed the document mid-answer.
          if (evt?.type === 'token' && evt.text) {
            draft += evt.text
            patchAnswer({ content: draft })
          } else if (evt?.type === 'proposal' && evt.proposal) {
            patchAnswer({ proposal: evt.proposal })
          } else if (evt?.type === 'document' && evt.document) {
            onDocUpdated(evt.document)
          } else if (evt?.type === 'tool' && evt.toolName) {
            upsertActivity(evt.toolName, evt.toolLabel, evt.toolStatus)
          }
        },
        complete: () => {
          setStreaming(false)
          subscriptionRef.current = null
          setMessages((prev) => {
            const last = prev[prev.length - 1]
            if (
              last?.role === 'assistant'
              && !last.content
              && !last.proposal
              && !(last.activities?.length > 0)
            ) {
              return prev.slice(0, -1)
            }
            return prev
          })
        },
        error: (err) => {
          setStreaming(false)
          subscriptionRef.current = null
          setError(err?.message || 'The answer stream failed.')
          // Drop the empty placeholder if nothing streamed in before the error.
          if (!draft) setMessages((prev) => prev.slice(0, -1))
        },
      })
  }

  function onKeyDown(e) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      send(e)
    }
  }

  async function resolveProposal(proposal, action) {
    const { messageId } = proposal
    setProposalBusyId(messageId)
    setError('')
    try {
      if (action === 'apply') {
        const updated = await applyEditProposal(docId, activeId, messageId)
        onDocUpdated(updated)
      } else {
        await rejectEditProposal(docId, activeId, messageId)
      }
      const status = action === 'apply' ? 'applied' : 'rejected'
      setMessages((prev) =>
        prev.map((m) =>
          m.proposal?.messageId === messageId
            ? { ...m, proposal: { ...m.proposal, status } }
            : m,
        ),
      )
    } catch (err) {
      // 409 = already resolved, or the document changed under the proposal.
      setError(err?.message || 'Could not update the proposal.')
    } finally {
      setProposalBusyId(null)
    }
  }

  const statusText = loadingConvos
    ? 'LOADING'
    : status === 'error'
      ? 'OFFLINE'
      : streaming
        ? 'STREAMING'
        : status === 'connecting'
          ? 'CONNECTING'
          : 'READY'

  const live = streaming || (!loadingConvos && status === 'ready')

  return (
    <div className="chat-layout">
      <aside className="chat-sidebar">
        <div className="chat-sidebar-head">
          <span className="chat-sidebar-title">Conversations</span>
          <button
            type="button"
            className="chat-new-btn"
            onClick={newChat}
            disabled={streaming}
            aria-label="New conversation"
          >
            <PlusIcon size={13} />
            <span>NEW</span>
          </button>
        </div>

        <div className="chat-convo-list">
          {loadingConvos ? (
            <div className="gen-empty">// LOADING…</div>
          ) : conversations.length === 0 ? (
            <div className="gen-empty">// NO_CHATS_YET</div>
          ) : (
            conversations.map((c) => (
              <div
                key={c.id}
                className={`chat-convo${c.id === activeId ? ' active' : ''}`}
              >
                <button
                  type="button"
                  className="chat-convo-main"
                  onClick={() => selectConversation(c.id)}
                  disabled={streaming}
                >
                  <span className="chat-convo-title">{c.title}</span>
                  <span className="chat-convo-time">{formatTime(c.updatedAt)}</span>
                </button>

                {confirmingId === c.id ? (
                  <div className="chat-convo-confirm">
                    <button
                      type="button"
                      className="chat-convo-confirm-btn"
                      onClick={() => setConfirmingId(null)}
                      disabled={deletingId === c.id}
                    >
                      CANCEL
                    </button>
                    <button
                      type="button"
                      className="chat-convo-confirm-btn danger"
                      onClick={() => confirmDelete(c.id)}
                      disabled={deletingId === c.id}
                    >
                      {deletingId === c.id ? '…' : 'DELETE'}
                    </button>
                  </div>
                ) : (
                  <button
                    type="button"
                    className="chat-convo-del"
                    onClick={() => setConfirmingId(c.id)}
                    disabled={streaming}
                    aria-label="Delete conversation"
                  >
                    <TrashIcon size={13} />
                  </button>
                )}
              </div>
            ))
          )}
        </div>
      </aside>

      <div className="panel chat-panel">
        <div className="panel-bar">
          <div className="panel-title">Chat</div>
          <div className="chat-model-controls">
            <label htmlFor="chat-model-select">MODEL</label>
            <select
              id="chat-model-select"
              className="chat-model-select"
              value={selectedModelId}
              onChange={changeModel}
              disabled={streaming || models.length === 0}
            >
              {models.map((model) => (
                <option key={model.id} value={model.id} title={model.description || model.name}>
                  {model.description ? `${model.name} — ${model.description}` : model.name}
                </option>
              ))}
            </select>
            <div className={`panel-status${live ? ' live' : ''}`}>
              {live && <div className="status-dot" />}
              {statusText}
            </div>
          </div>
        </div>

        <div className="panel-body chat-body">
          <div className="chat-log" ref={logRef}>
            {loadingMessages ? (
              <div className="gen-empty">// LOADING…</div>
            ) : !activeId && conversations.length === 0 && !loadingConvos ? (
              <div className="gen-empty">
                // NO_CHATS_YET — type below to start your first conversation, or
                hit NEW. Answers are grounded in this document.
              </div>
            ) : messages.length === 0 ? (
              <div className="gen-empty">
                // NO_MESSAGES_YET — ask anything about this document. Answers are
                grounded in its content.
              </div>
            ) : (
              messages.map((m, i) => {
                const isUser = m.role === 'user'
                const isLast = i === messages.length - 1
                return (
                  <div
                    key={i}
                    className={`chat-msg ${isUser ? 'user' : 'assistant'}`}
                  >
                    <div className="chat-msg-label">{isUser ? '// YOU' : '// LUMINA'}</div>
                    {!isUser && m.activities && m.activities.length > 0 && (
                      <div className="chat-activities">
                        {m.activities.map((a) => (
                          <div key={a.id} className="chat-activity">
                            <span className="chat-activity-mark" />
                            <span>{a.label}</span>
                          </div>
                        ))}
                      </div>
                    )}
                    <div className="chat-msg-text">
                      <ReactMarkdown remarkPlugins={[remarkGfm]}>
                        {m.content}
                      </ReactMarkdown>
                      {!isUser && isLast && streaming && <span className="chat-caret" />}
                    </div>
                    {!isUser && m.proposal && (
                      <div className="chat-proposal">
                        <div className="chat-proposal-head">
                          <span>
                            {m.proposal.kind === 'insert'
                              ? '// PROPOSED_ADDITION'
                              : m.proposal.kind === 'text'
                                ? '// SOURCE_TEXT_CHANGE'
                              : '// PROPOSED_EDIT'}
                          </span>
                          <span className={`chat-proposal-status ${m.proposal.status}`}>
                            {m.proposal.status.toUpperCase()}
                          </span>
                        </div>
                        {m.proposal.kind === 'structured'
                          && !(m.proposal.targetId && m.proposal.placement) ? (
                          <>
                            <div className="chat-proposal-caption">// CHANGE SUMMARY</div>
                            {m.proposal.target && (
                              <pre className="chat-proposal-block ctx">{m.proposal.target}</pre>
                            )}
                            {m.proposal.replacement && (
                              m.proposal.replacement.startsWith('Deleted content:') ? (
                                <><div className="chat-proposal-caption">// DELETED</div><pre className="chat-proposal-block del">{m.proposal.replacement.slice('Deleted content:'.length).trim()}</pre></>
                              ) : <><div className="chat-proposal-caption">// PROPOSED CHANGES</div><pre className="chat-proposal-block ctx">{m.proposal.replacement}</pre></>
                            )}
                          </>
                        ) : m.proposal.kind === 'text' ? (
                          <>
                            {m.proposal.target && <><div className="chat-proposal-caption">// CHANGE SUMMARY</div><pre className="chat-proposal-block ctx">{m.proposal.target}</pre></>}
                            <div className="chat-proposal-caption">// SOURCE TEXT CHANGE</div>
                            {getTextProposalChanges(m.proposal).map((change, changeIndex) => (
                              <div className="chat-proposal-change" key={changeIndex}>
                                {change.target && <><div className="chat-proposal-caption">// TARGET · TO BE REPLACED</div><pre className="chat-proposal-block del">{change.target}</pre></>}
                                {change.replacement && <><div className="chat-proposal-caption">// REPLACEMENT</div><pre className="chat-proposal-block add">{change.replacement}</pre></>}
                              </div>
                            ))}
                            {!getTextProposalChanges(m.proposal).length && m.proposal.replacement && <><div className="chat-proposal-caption">// PROPOSED CHANGE</div><pre className="chat-proposal-block ctx">{m.proposal.replacement}</pre></>}
                          </>
                        ) : m.proposal.kind === 'insert' ? (
                          // An insertion: the anchor (if any) is context, not a
                          // deletion — the new text is added after it.
                          <>
                            {m.proposal.target && (
                              <>
                                <div className="chat-proposal-caption">// INSERT AFTER THIS</div>
                                <pre className="chat-proposal-block ctx">{m.proposal.target}</pre>
                              </>
                            )}
                            {m.proposal.replacement && (
                              <><div className="chat-proposal-caption">// ADDED</div><pre className="chat-proposal-block add">{m.proposal.replacement}</pre></>
                            )}
                          </>
                        ) : m.proposal.kind === 'replace_node' ? (
                          <>
                            {m.proposal.target && <><div className="chat-proposal-caption">// REPLACES THIS</div><pre className="chat-proposal-block del">{m.proposal.target}</pre></>}
                            <div className="chat-proposal-caption">// REPLACED WITH</div>
                            {m.proposal.replacement && (
                              <pre className="chat-proposal-block add">{m.proposal.replacement}</pre>
                            )}
                          </>
                        ) : m.proposal.kind === 'insert_node' ? (
                          <>
                            <div className="chat-proposal-caption">// ADDED</div>
                            {m.proposal.replacement && (
                              <pre className="chat-proposal-block add">{m.proposal.replacement}</pre>
                            )}
                          </>
                        ) : m.proposal.kind === 'delete_node' ? (
                          <>
                            <div className="chat-proposal-caption">// DELETED</div>
                            {m.proposal.target && (
                              <pre className="chat-proposal-block del">{m.proposal.target}</pre>
                            )}
                          </>
                        ) : m.proposal.kind === 'move_node'
                          || (m.proposal.kind === 'structured'
                            && m.proposal.targetId
                            && m.proposal.placement) ? (
                          (() => {
                            const targetImage = imageNodes.find(
                              (node) => node.attrs?.id === m.proposal.targetId,
                            )
                            const destinationImage = imageNodes.find(
                              (node) => node.attrs?.id === m.proposal.parentId,
                            ) || imageNodes.find(
                              (node) => node.attrs?.id !== m.proposal.targetId,
                            )
                            const isImageMove = Boolean(targetImage || destinationImage)
                            return (
                              <>
                                <div className="chat-proposal-caption">// NODE MOVE</div>
                                {isImageMove ? (
                                  <div className="chat-proposal-image-move">
                                    <ProposalAssetPreview
                                      documentId={docId}
                                      node={targetImage}
                                      label="// MOVE THIS IMAGE"
                                    />
                                    <div className="chat-proposal-move-arrow">
                                      {m.proposal.placement === 'before' ? '↑ BEFORE' : '↓ AFTER'}
                                    </div>
                                    <ProposalAssetPreview
                                      documentId={docId}
                                      node={destinationImage}
                                      label="// RELATIVE TO THIS IMAGE"
                                    />
                                  </div>
                                ) : (
                                  <>
                                    {m.proposal.target && (
                                      <>
                                        <div className="chat-proposal-caption">// MOVE THIS</div>
                                        <pre className="chat-proposal-block del">{m.proposal.target}</pre>
                                      </>
                                    )}
                                    {m.proposal.replacement && (
                                      <>
                                        <div className="chat-proposal-caption">// TO</div>
                                        <pre className="chat-proposal-block add">{m.proposal.replacement}</pre>
                                      </>
                                    )}
                                  </>
                                )}
                              </>
                            )
                          })()
                        ) : m.proposal.kind === 'change_list_type' ? (
                          <>
                            <div className="chat-proposal-caption">// CURRENT LIST</div>
                            {m.proposal.target && (
                              <pre className="chat-proposal-block ctx">{m.proposal.target}</pre>
                            )}
                            {m.proposal.replacement && (
                              <>
                                <div className="chat-proposal-caption">// CHANGES TO</div>
                                <pre className="chat-proposal-block add">{m.proposal.replacement}</pre>
                              </>
                            )}
                          </>
                        ) : (
                          <>
                            {m.proposal.target && (
                              <><div className="chat-proposal-caption">// REMOVED</div><pre className="chat-proposal-block del">{m.proposal.target}</pre></>
                            )}
                            {m.proposal.replacement && (
                              <><div className="chat-proposal-caption">// REPLACED WITH</div><pre className="chat-proposal-block add">{m.proposal.replacement}</pre></>
                            )}
                          </>
                        )}
                        {m.proposal.status === 'pending' && (
                          <div className="chat-proposal-actions">
                            <button
                              type="button"
                              className="btn primary"
                              onClick={() => resolveProposal(m.proposal, 'apply')}
                              disabled={proposalBusyId !== null || streaming}
                            >
                              {proposalBusyId === m.proposal.messageId
                                ? 'Applying…'
                                : 'Apply'}
                            </button>
                            <button
                              type="button"
                              className="btn ghost"
                              onClick={() => resolveProposal(m.proposal, 'reject')}
                              disabled={proposalBusyId !== null || streaming}
                            >
                              Reject
                            </button>
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )
              })
            )}
          </div>

          {error && <div className="error-line">// ERROR: {error}</div>}

          <form className="chat-form" onSubmit={send}>
            <textarea
              className="chat-input"
              rows={1}
              placeholder={
                status === 'error'
                  ? 'Connection unavailable…'
                  : 'Ask about this document…'
              }
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={onKeyDown}
              disabled={status === 'error'}
            />
            <button
              type="submit"
              className="btn primary"
              disabled={!input.trim() || streaming || status !== 'ready'}
            >
              {streaming ? 'Streaming…' : 'Send'}
            </button>
          </form>
        </div>
      </div>
    </div>
  )
}
