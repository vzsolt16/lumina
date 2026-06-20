import { useEffect, useState } from 'react'
import { useParams, useOutletContext } from 'react-router-dom'

/**
 * Shared body for the flashcards and quiz tabs. On entry it fetches any
 * persisted result; if none exists the user can generate one. A live job
 * (SignalR) and a persisted result share the same shape, so `renderResult`
 * handles both.
 *
 * @param {object}   props
 * @param {string}   props.kind          'flashcard' | 'quiz' (job hook key)
 * @param {string}   props.title         panel title
 * @param {Function} props.fetchExisting (docId) => Promise<normalizedResult|null>
 * @param {Function} props.renderResult  (result) => ReactNode
 * @param {string}   props.noun          label noun, e.g. 'flashcards' / 'quiz'
 * @param {string}   props.emptyHint     hint shown when nothing is generated yet
 */
export default function GenerationTab({
  kind,
  title,
  fetchExisting,
  renderResult,
  noun,
  emptyHint,
}) {
  const { docId } = useParams()
  // The job is owned by the workspace (StudioDocument) so it survives tab
  // switches and keeps streaming progress in the background.
  const job = useOutletContext()[kind]
  const [existing, setExisting] = useState(undefined) // undefined = loading

  useEffect(() => {
    let cancelled = false
    setExisting(undefined)
    fetchExisting(docId)
      .then((res) => {
        if (!cancelled) setExisting(res ?? null)
      })
      .catch(() => {
        if (!cancelled) setExisting(null)
      })
    return () => {
      cancelled = true
    }
    // fetchExisting is stable; re-run only on doc change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [docId])

  const running = job.status === 'starting' || job.status === 'processing'
  const loading = existing === undefined
  const result = job.status === 'completed' ? job.result : existing
  const hasResult = Array.isArray(result) ? result.length > 0 : !!result

  let statusText = 'IDLE'
  if (loading) statusText = 'LOADING'
  else if (job.status === 'starting') statusText = 'STARTING'
  else if (job.status === 'processing') statusText = 'PROCESSING'
  else if (job.status === 'completed') statusText = 'COMPLETED'
  else if (job.status === 'failed') statusText = 'FAILED'
  else if (hasResult) statusText = 'LOADED'

  const live = running || job.status === 'completed'

  return (
    <div>
      <div className="studio-actions">
        <button
          className="btn primary"
          onClick={() => job.start(docId)}
          disabled={loading || running}
        >
          {hasResult ? `Regenerate ${noun}` : `Generate ${noun}`}
        </button>
      </div>

      <div className="panel" style={{ marginTop: '8px' }}>
        <div className="panel-bar">
          <div className="panel-title">{title}</div>
          <div className={`panel-status${live ? ' live' : ''}`}>
            {live && <div className="status-dot" />}
            {statusText}
          </div>
        </div>
        <div className="panel-body">
          {loading && <div className="gen-empty">// LOADING…</div>}

          {running && (
            <div className="progress-wrap">
              <div className="progress-meta">
                <span>{job.message || 'WORKING…'}</span>
                <span>{job.progress}%</span>
              </div>
              <div className="progress-track">
                <div className="progress-fill" style={{ width: `${job.progress}%` }} />
              </div>
            </div>
          )}

          {job.status === 'failed' && (
            <div className="error-line">// ERROR: {job.error}</div>
          )}

          {!loading && !running && job.status !== 'failed' && (
            hasResult ? renderResult(result) : <div className="gen-empty">{emptyHint}</div>
          )}
        </div>
      </div>
    </div>
  )
}
