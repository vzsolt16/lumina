// Glass panel that frames one generation job: title bar with live status,
// a progress bar while running, and the rendered result (or error) when done.
export default function GenerationPanel({ title, job, emptyHint, children }) {
  const { status, progress, message, error } = job
  const running = status === 'starting' || status === 'processing'
  const live = running || status === 'completed'

  let statusText = 'IDLE'
  if (status === 'starting') statusText = 'STARTING'
  else if (status === 'processing') statusText = 'PROCESSING'
  else if (status === 'completed') statusText = 'COMPLETED'
  else if (status === 'failed') statusText = 'FAILED'

  return (
    <div className="panel">
      <div className="panel-bar">
        <div className="panel-title">{title}</div>
        <div className={`panel-status${live ? ' live' : ''}`}>
          {live && <div className="status-dot" />}
          {statusText}
        </div>
      </div>
      <div className="panel-body">
        {status === 'idle' && <div className="gen-empty">{emptyHint}</div>}

        {running && (
          <div className="progress-wrap">
            <div className="progress-meta">
              <span>{message || 'WORKING…'}</span>
              <span>{progress}%</span>
            </div>
            <div className="progress-track">
              <div className="progress-fill" style={{ width: `${progress}%` }} />
            </div>
          </div>
        )}

        {status === 'failed' && (
          <div className="error-line">// ERROR: {error}</div>
        )}

        {status === 'completed' && children}
      </div>
    </div>
  )
}
