import { useEffect, useState } from 'react'
import { useParams, NavLink, Link, Outlet } from 'react-router-dom'
import Nav from '../components/Nav.jsx'
import Footer from '../components/Footer.jsx'
import { FlashcardsIcon, QuizIcon, ChatIcon, FileIcon, NotesIcon } from '../components/icons.jsx'
import { useDocumentJobs } from '../context/JobsContext.jsx'
import { getDocument } from '../api/client.js'
import './Studio.css'

const TABS = [
  { to: 'content', label: 'Content', Icon: NotesIcon },
  { to: 'flashcards', label: 'Flashcards', Icon: FlashcardsIcon },
  { to: 'quiz', label: 'Quiz', Icon: QuizIcon },
  { to: 'chat', label: 'Chat', Icon: ChatIcon },
]

// Layout for a single document: header + tab bar, with the active tab
// (flashcards / quiz / chat) rendered into the <Outlet/>.
export default function StudioDocument() {
  const { docId } = useParams()
  const [doc, setDoc] = useState(null)
  const [error, setError] = useState('')

  // Jobs come from the app-level registry, keyed by document. They keep running
  // and streaming progress across tab and document navigation; the registry
  // owns their lifecycle, so we never reset them on doc change here.
  const { flashcard, quiz } = useDocumentJobs(docId)

  useEffect(() => {
    let cancelled = false
    setDoc(null)
    setError('')
    getDocument(docId)
      .then((d) => {
        if (!cancelled) setDoc(d)
      })
      .catch((err) => {
        if (!cancelled) setError(err?.message || 'Document not found.')
      })
    return () => {
      cancelled = true
    }
  }, [docId])

  return (
    <>
      <Nav showSectionLinks={false} />

      <div className="studio">
        <div className="doc-ws-head">
          <Link to="/studio" className="doc-back">‹ LIBRARY</Link>
          <div className="doc-ws-title">
            <span className="doc-card-icon">
              <FileIcon size={20} />
            </span>
            <span className="doc-file">
              {doc ? doc.fileName : error ? 'Unavailable' : 'Loading…'}
            </span>
          </div>
          {error && <div className="error-line">// ERROR: {error}</div>}
        </div>

        <div className="doc-tabs">
          {TABS.map(({ to, label, Icon }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) => `doc-tab${isActive ? ' active' : ''}`}
            >
              <Icon size={16} />
              <span>{label}</span>
            </NavLink>
          ))}
        </div>

        <div className="doc-tab-body">
          <Outlet context={{ flashcard, quiz, doc, onDocUpdated: setDoc }} />
        </div>
      </div>

      <Footer />
    </>
  )
}
