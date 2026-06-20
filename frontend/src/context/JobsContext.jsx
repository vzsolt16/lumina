import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useSyncExternalStore,
} from 'react'
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr'
import { generateFlashcards, generateQuiz } from '../api/client.js'
import { getAccessToken } from '../api/authToken.js'
import { useAuth } from './AuthContext.jsx'

// App-level registry of generation jobs, keyed by document id. Each document
// owns a flashcard job and a quiz job, each with its own SignalR connection.
// Because the store lives above the router, jobs keep streaming progress and
// completing while the user navigates between tabs or even other documents.
//
// Rendering is wired through useSyncExternalStore with a per-document snapshot,
// so a progress tick on document A only re-renders components viewing A.

// Per-kind hub wiring (mirrors the old useGenerationJob config).
const KINDS = {
  flashcard: {
    hubUrl: '/ws/flashcard',
    joinMethod: 'JoinFlashcardGroup',
    generate: generateFlashcards,
    idKey: 'flashcardJobId',
    resultKey: 'flashcards',
  },
  quiz: {
    hubUrl: '/ws/quiz',
    joinMethod: 'JoinQuizGroup',
    generate: generateQuiz,
    idKey: 'quizId',
    resultKey: 'quiz',
  },
}

const INITIAL = {
  status: 'idle', // idle | starting | processing | completed | failed
  progress: 0,
  message: '',
  result: null,
  error: null,
}

// Shared, stable reference for documents with no job state yet. Returning the
// same object keeps useSyncExternalStore from looping on unchanged snapshots.
const EMPTY_DOC = { flashcard: INITIAL, quiz: INITIAL }

function createJobsStore() {
  let state = {} // { [docId]: { flashcard: JobState, quiz: JobState } }
  const connections = new Map() // `${docId}:${kind}` -> HubConnection
  const viewers = new Map() // docId -> mounted-consumer count
  const listeners = new Set()

  const emit = () => {
    for (const listener of listeners) listener()
  }

  const subscribe = (listener) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
  }

  const getDoc = (docId) => state[docId] || EMPTY_DOC

  function setJob(docId, kind, patch) {
    const doc = state[docId] || EMPTY_DOC
    const current = doc[kind]
    const next = typeof patch === 'function' ? patch(current) : { ...current, ...patch }
    state = { ...state, [docId]: { ...doc, [kind]: next } }
    emit()
  }

  const connKey = (docId, kind) => `${docId}:${kind}`

  function stopConnection(docId, kind) {
    const key = connKey(docId, kind)
    const conn = connections.get(key)
    if (conn) {
      conn.stop().catch(() => {})
      connections.delete(key)
    }
  }

  const isRunning = (docId, kind) => {
    const s = state[docId]?.[kind]?.status
    return s === 'starting' || s === 'processing'
  }

  // Free a document's state/connections once nothing references it and no job
  // is still in flight. Results are persisted server-side and re-fetched on
  // return, so dropping idle entries is safe and keeps the registry small.
  function maybeDrop(docId) {
    if (viewers.get(docId)) return
    if (isRunning(docId, 'flashcard') || isRunning(docId, 'quiz')) return
    if (!state[docId]) return
    stopConnection(docId, 'flashcard')
    stopConnection(docId, 'quiz')
    const next = { ...state }
    delete next[docId]
    state = next
    emit()
  }

  async function start(docId, kind) {
    const cfg = KINDS[kind]
    stopConnection(docId, kind)
    setJob(docId, kind, { ...INITIAL, status: 'starting' })

    try {
      const connection = new HubConnectionBuilder()
        .withUrl(cfg.hubUrl, {
          skipNegotiation: true,
          transport: HttpTransportType.WebSockets,
          accessTokenFactory: () => getAccessToken(),
        })
        .withAutomaticReconnect()
        .build()

      connection.on('Progress', (payload) => {
        setJob(docId, kind, (s) => ({
          ...s,
          status: 'processing',
          progress: payload.progress ?? s.progress,
          message: payload.message ?? s.message,
        }))
      })

      connection.on('Completed', (payload) => {
        setJob(docId, kind, (s) => ({
          ...s,
          status: 'completed',
          progress: 100,
          message: 'Done',
          result: payload[cfg.resultKey],
        }))
        stopConnection(docId, kind)
        maybeDrop(docId)
      })

      connection.on('Failed', (payload) => {
        setJob(docId, kind, (s) => ({
          ...s,
          status: 'failed',
          error: payload?.error || 'Generation failed.',
        }))
        stopConnection(docId, kind)
        maybeDrop(docId)
      })

      connections.set(connKey(docId, kind), connection)
      await connection.start()

      const res = await cfg.generate(docId)
      const jobId = res[cfg.idKey]
      await connection.invoke(cfg.joinMethod, String(jobId))

      setJob(docId, kind, (s) => ({ ...s, status: 'processing', message: 'Queued…' }))
    } catch (err) {
      stopConnection(docId, kind)
      setJob(docId, kind, (s) => ({
        ...s,
        status: 'failed',
        error: err?.message || 'Could not start generation.',
      }))
    }
  }

  function reset(docId, kind) {
    stopConnection(docId, kind)
    setJob(docId, kind, { ...INITIAL })
  }

  function retain(docId) {
    viewers.set(docId, (viewers.get(docId) || 0) + 1)
  }

  function release(docId) {
    const n = (viewers.get(docId) || 0) - 1
    if (n <= 0) {
      viewers.delete(docId)
      maybeDrop(docId)
    } else {
      viewers.set(docId, n)
    }
  }

  function clearAll() {
    for (const conn of connections.values()) conn.stop().catch(() => {})
    connections.clear()
    viewers.clear()
    state = {}
    emit()
  }

  return { subscribe, getDoc, start, reset, retain, release, clearAll }
}

const JobsContext = createContext(null)

export function JobsProvider({ children }) {
  const storeRef = useRef(null)
  if (!storeRef.current) storeRef.current = createJobsStore()
  const store = storeRef.current

  // End all jobs/connections when the session ends (logout or silent-refresh
  // expiry). Guard on the previous value so a fresh login doesn't clear.
  const { user } = useAuth()
  const prevUser = useRef(user)
  useEffect(() => {
    if (prevUser.current && !user) store.clearAll()
    prevUser.current = user
  }, [user, store])

  return <JobsContext.Provider value={store}>{children}</JobsContext.Provider>
}

/**
 * Subscribe to a single document's jobs. Returns { flashcard, quiz }, each the
 * job state spread with bound start()/reset(). Re-renders only when this
 * document's state changes.
 */
export function useDocumentJobs(docId) {
  const store = useContext(JobsContext)
  if (!store) throw new Error('useDocumentJobs must be used within a JobsProvider')

  const slice = useSyncExternalStore(store.subscribe, () => store.getDoc(docId))

  // Ref-count viewers so the store can drop this document when nobody's looking.
  useEffect(() => {
    store.retain(docId)
    return () => store.release(docId)
  }, [store, docId])

  return useMemo(
    () => ({
      flashcard: {
        ...slice.flashcard,
        start: (id = docId) => store.start(id, 'flashcard'),
        reset: () => store.reset(docId, 'flashcard'),
      },
      quiz: {
        ...slice.quiz,
        start: (id = docId) => store.start(id, 'quiz'),
        reset: () => store.reset(docId, 'quiz'),
      },
    }),
    [slice, store, docId],
  )
}
