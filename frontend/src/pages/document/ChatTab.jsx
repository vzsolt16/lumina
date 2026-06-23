import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr'
import { getChatHistory } from '../../api/client.js'
import { getAccessToken } from '../../api/authToken.js'

// Chat is a direct request/response stream, not a background job, so unlike
// flashcards/quiz it doesn't live in JobsContext — the connection and message
// state are owned by this tab and torn down when it unmounts. History is loaded
// from the REST endpoint on mount; new answers stream token-by-token over the
// /ws/chat hub via connection.stream('StreamAnswer', docId, question).

export default function ChatTab() {
  const { docId } = useParams()

  const [messages, setMessages] = useState([]) // { role, content }[]
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState('connecting') // connecting | ready | error
  const [streaming, setStreaming] = useState(false)
  const [input, setInput] = useState('')
  const [error, setError] = useState('')

  const connectionRef = useRef(null)
  const subscriptionRef = useRef(null)
  const logRef = useRef(null)

  // Load history + open the hub connection for this document.
  useEffect(() => {
    let cancelled = false

    setMessages([])
    setLoading(true)
    setError('')
    setStatus('connecting')

    getChatHistory(docId)
      .then((history) => {
        if (!cancelled) {
          setMessages(
            (history || []).map((m) => ({ role: m.role, content: m.content })),
          )
        }
      })
      .catch(() => {
        if (!cancelled) setError('Could not load chat history.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
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
    connection
      .start()
      .then(() => !cancelled && setStatus('ready'))
      .catch(() => !cancelled && setStatus('error'))

    return () => {
      cancelled = true
      subscriptionRef.current?.dispose()
      subscriptionRef.current = null
      connection.stop().catch(() => {})
      connectionRef.current = null
    }
  }, [docId])

  // Keep the log pinned to the latest message as it grows.
  useEffect(() => {
    const el = logRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages])

  function send(e) {
    e.preventDefault()
    const question = input.trim()
    if (!question || streaming || status !== 'ready') return

    setError('')
    setInput('')
    setStreaming(true)

    // Optimistically show the question and an empty assistant slot we fill in
    // as tokens arrive. `draft` accumulates outside state to dodge stale closures.
    setMessages((prev) => [
      ...prev,
      { role: 'user', content: question },
      { role: 'assistant', content: '' },
    ])

    let draft = ''
    const setAnswer = (content) =>
      setMessages((prev) => {
        const next = prev.slice()
        next[next.length - 1] = { role: 'assistant', content }
        return next
      })

    subscriptionRef.current = connectionRef.current
      .stream('StreamAnswer', docId, question)
      .subscribe({
        next: (token) => {
          draft += token
          setAnswer(draft)
        },
        complete: () => {
          setStreaming(false)
          subscriptionRef.current = null
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

  const statusText = loading
    ? 'LOADING'
    : status === 'error'
      ? 'OFFLINE'
      : streaming
        ? 'STREAMING'
        : status === 'connecting'
          ? 'CONNECTING'
          : 'READY'

  const live = streaming || (!loading && status === 'ready')

  return (
    <div className="panel chat-panel">
      <div className="panel-bar">
        <div className="panel-title">Chat</div>
        <div className={`panel-status${live ? ' live' : ''}`}>
          {live && <div className="status-dot" />}
          {statusText}
        </div>
      </div>

      <div className="panel-body chat-body">
        <div className="chat-log" ref={logRef}>
          {loading ? (
            <div className="gen-empty">// LOADING…</div>
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
                  <div className="chat-msg-text">
                    {m.content}
                    {!isUser && isLast && streaming && <span className="chat-caret" />}
                  </div>
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
  )
}
