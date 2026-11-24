import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import Nav from '../components/Nav.jsx'
import { getRagExecution, getRagExecutions } from '../api/client.js'
import './RagInspector.css'

const pretty = (value) => value == null ? 'Unavailable' : value
const formatResult = (value) => {
  if (!value) return ''
  try { return JSON.stringify(JSON.parse(value), null, 2) } catch { return value }
}

export default function RagInspector() {
  const [filters, setFilters] = useState({ status: '', conversationId: '' })
  const [page, setPage] = useState(1)
  const [result, setResult] = useState({ items: [], total: 0, pageSize: 20 })
  const [selected, setSelected] = useState(null)
  const [error, setError] = useState('')
  useEffect(() => {
    getRagExecutions({ ...filters, page, pageSize: 20 }).then(setResult).catch(e => setError(e.message))
  }, [filters, page])
  async function open(id) {
    try { setSelected(await getRagExecution(id)); setError('') } catch (e) { setError(e.message) }
  }
  let details = {}
  try { details = selected?.contextJson ? JSON.parse(selected.contextJson) : {} } catch { details = {} }
  const initial = details.initialContext || details
  const toolRetrievals = details.toolRetrievals || []
  return <><Nav showSectionLinks={false}/><main className="rag-inspector">
    <Link to="/studio">← Studio</Link><h1>RAG Inspector</h1><p>Inspect retrieval and generation details for your recent chat requests.</p>
    {error && <p role="alert">{error}</p>}
    <section><label>Status <select value={filters.status} onChange={e => { setPage(1); setFilters(v => ({ ...v, status: e.target.value })) }}><option value="">All</option><option>Completed</option><option>Failed</option><option>Cancelled</option></select></label>
      <label>Conversation ID <input value={filters.conversationId} onChange={e => { setPage(1); setFilters(v => ({ ...v, conversationId: e.target.value })) }}/></label></section>
    <table><thead><tr><th>Time</th><th>Query</th><th>Status</th><th>Duration</th><th>Retrieved passages</th><th>Model</th></tr></thead><tbody>
      {result.items.map(x => <tr key={x.id} onClick={() => open(x.id)}><td>{new Date(x.startedAt).toLocaleString()}</td><td>{x.query}</td><td>{x.status}</td><td>{x.durationMs} ms</td><td>{x.retrievedChunkCount}</td><td>{x.modelId || '—'}</td></tr>)}
    </tbody></table>
    <button disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Previous</button> Page {page} of {Math.max(1, Math.ceil(result.total / result.pageSize))} <button disabled={page * result.pageSize >= result.total} onClick={() => setPage(p => p + 1)}>Next</button>
    {selected && <article><h2>Execution details</h2>
      <details open><summary>Request</summary><p>{selected.query}</p><p>{selected.id} · conversation {selected.conversationId} · {selected.status} · {new Date(selected.startedAt).toLocaleString()}</p></details>
      <details open><summary>Initial context · {pretty(initial.strategy || initial.mode)} · {pretty(initial.outcome)}</summary>
        <p>Complete: {String(initial.complete ?? 'Unavailable')} · version {pretty(initial.documentVersion)} · {initial.approximateContextTokens ?? initial.approxTokens ?? '—'} estimated tokens · {initial.retrievedChunkCount ?? initial.chunks?.length ?? 0} chunks</p>
        {initial.strategy === 'full' || initial.mode === 'full' ? <p>The complete document was supplied as context; this initial strategy does not represent chunk retrieval.</p> : null}
        {(initial.sources || initial.chunks || []).map((c, index) => <p key={`${c.documentId}-${c.chunkIndex ?? index}`}><b>Initial source #{c.rank ?? index + 1}</b> — {c.documentName}, chunk {c.chunkIndex ?? '—'}, score {pretty(c.relevanceScore)}, version {pretty(c.documentVersion)}</p>)}</details>
      <details open><summary>Tool retrievals · {toolRetrievals.length}</summary>
        {toolRetrievals.length === 0 ? <p>No retrieval tools were called.</p> : toolRetrievals.map(item => <details key={`${item.order}-${item.toolName}`}>
          <summary>Tool retrieval #{item.order}: {item.strategy} · {item.outcome} · {item.acceptedCandidateCount ?? item.returnedSourceCount} accepted · {item.durationMs} ms</summary>
          {item.query && <p><b>Query:</b> {item.query}</p>}
          {item.arguments && <p><b>Arguments:</b> <code>{item.arguments}</code></p>}
          <p>Candidates: {pretty(item.candidateCount)} · accepted: {pretty(item.acceptedCandidateCount)} · rejected: {pretty(item.rejectedCandidateCount)} · returned sources: {item.returnedSourceCount}</p>
          {item.sources?.map((source, index) => <p key={`${source.documentId}-${source.chunkIndex}-${index}`}><b>Source #{index + 1}</b> — {source.documentName}, chunk {source.chunkIndex}, score {pretty(source.relevanceScore)}, heading {(source.headingPath || []).join(' › ') || '—'}</p>)}
          {item.passages?.map((passage, index) => <pre key={`passage-${index}`}>{passage}</pre>)}
          {item.result && <pre>{formatResult(item.result)}</pre>}
        </details>)}</details>
      <details><summary>LLM context</summary><pre>{initial.assembledContext || details.assembledContext || 'No context captured'}</pre></details>
      {details.sourceAccounting && <details><summary>Source accounting</summary><p>Initial context sources: {details.sourceAccounting.initialContextSources} · tool retrieved sources: {details.sourceAccounting.toolRetrievedSources} · tool passages accepted: {details.sourceAccounting.toolRetrievedPassages} · final answer citations: {pretty(details.sourceAccounting.finalAnswerCitations)}</p></details>}
      <details open><summary>Generation</summary><p>{selected.modelId} · {selected.generationDurationMs ?? '—'} ms · input {pretty(selected.inputTokens)} / output {pretty(selected.outputTokens)} tokens</p><pre>{selected.answer || 'No answer captured'}</pre></details>
      {details.providerRequests && <details open><summary>Provider requests · {details.providerRequests.length} · cumulative {details.cumulativeUsage?.inputTokens ?? selected.inputTokens ?? '—'} input / {details.cumulativeUsage?.outputTokens ?? selected.outputTokens ?? '—'} output / {details.cumulativeUsage?.totalTokens ?? '—'} total tokens</summary>
        <table><thead><tr><th>Request</th><th>Input</th><th>Output</th><th>Total</th><th>Tool calls</th></tr></thead><tbody>{details.providerRequests.map(request => <tr key={request.requestNumber}><td>{request.requestNumber}</td><td>{pretty(request.inputTokens)}</td><td>{pretty(request.outputTokens)}</td><td>{pretty(request.totalTokens)}</td><td>{request.toolCalls?.join(', ') || '—'}</td></tr>)}</tbody></table>
      </details>}
      <details><summary>Timing</summary><p>Retrieval {selected.retrievalDurationMs ?? '—'} ms · generation {selected.generationDurationMs ?? '—'} ms · total {selected.durationMs} ms</p></details>
      {selected.failureStage && <p>Failure stage: {selected.failureStage}</p>}
    </article>}
  </main></>
}
