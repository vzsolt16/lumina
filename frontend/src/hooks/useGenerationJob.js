import { useCallback, useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr'
import { generateFlashcards, generateQuiz } from '../api/client.js'
import { getAccessToken } from '../api/authToken.js'

// Per-kind wiring for the two SignalR hubs. Both emit Progress/Completed/Failed
// and expect the client to join a group keyed by the job id after connecting.
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

/**
 * Drives one generation job (flashcards or quiz) end-to-end:
 * connect hub -> POST generate -> join group -> stream Progress -> Completed.
 */
export default function useGenerationJob(kind) {
  const cfg = KINDS[kind]
  const [state, setState] = useState(INITIAL)
  const connectionRef = useRef(null)

  const cleanup = useCallback(() => {
    if (connectionRef.current) {
      connectionRef.current.stop().catch(() => {})
      connectionRef.current = null
    }
  }, [])

  // Tear down the hub connection if the component unmounts mid-job.
  useEffect(() => cleanup, [cleanup])

  const reset = useCallback(() => {
    cleanup()
    setState(INITIAL)
  }, [cleanup])

  const start = useCallback(
    async (documentId) => {
      cleanup()
      setState({ ...INITIAL, status: 'starting' })

      try {
        const connection = new HubConnectionBuilder()
          .withUrl(cfg.hubUrl, {
            // WebSockets only — skips the negotiate round-trip and keeps the
            // dev proxy simple. The hub is [Authorize]'d, so SignalR appends the
            // access token as the `access_token` query param (the only way to
            // auth a WebSocket handshake from the browser).
            skipNegotiation: true,
            transport: HttpTransportType.WebSockets,
            accessTokenFactory: () => getAccessToken(),
          })
          .withAutomaticReconnect()
          .build()

        connection.on('Progress', (payload) => {
          setState((s) => ({
            ...s,
            status: 'processing',
            progress: payload.progress ?? s.progress,
            message: payload.message ?? s.message,
          }))
        })

        connection.on('Completed', (payload) => {
          setState((s) => ({
            ...s,
            status: 'completed',
            progress: 100,
            message: 'Done',
            result: payload[cfg.resultKey],
          }))
          cleanup()
        })

        connection.on('Failed', (payload) => {
          setState((s) => ({
            ...s,
            status: 'failed',
            error: payload?.error || 'Generation failed.',
          }))
          cleanup()
        })

        connectionRef.current = connection
        await connection.start()

        // POST kicks off the background job; the response carries the job id
        // we need to join the right SignalR group.
        const res = await cfg.generate(documentId)
        const jobId = res[cfg.idKey]
        await connection.invoke(cfg.joinMethod, String(jobId))

        setState((s) => ({ ...s, status: 'processing', message: 'Queued…' }))
      } catch (err) {
        cleanup()
        setState((s) => ({
          ...s,
          status: 'failed',
          error: err?.message || 'Could not start generation.',
        }))
      }
    },
    [cfg, cleanup],
  )

  return { ...state, start, reset }
}
