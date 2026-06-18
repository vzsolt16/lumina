import { useState } from 'react'
import Nav from '../components/Nav.jsx'
import Footer from '../components/Footer.jsx'
import FileUpload from '../components/FileUpload.jsx'
import GenerationPanel from '../components/GenerationPanel.jsx'
import FlashcardDeck from '../components/FlashcardDeck.jsx'
import QuizView from '../components/QuizView.jsx'
import useGenerationJob from '../hooks/useGenerationJob.js'
import { uploadDocument } from '../api/client.js'
import './Studio.css'

export default function Studio() {
  const [doc, setDoc] = useState(null) // { id, fileName }
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState('')

  const flashcards = useGenerationJob('flashcard')
  const quiz = useGenerationJob('quiz')

  async function handleFile(file) {
    setUploading(true)
    setUploadError('')
    setDoc(null)
    flashcards.reset()
    quiz.reset()
    try {
      const result = await uploadDocument(file)
      setDoc(result)
    } catch (err) {
      setUploadError(err?.message || 'Upload failed.')
    } finally {
      setUploading(false)
    }
  }

  const busy =
    flashcards.status === 'starting' ||
    flashcards.status === 'processing' ||
    quiz.status === 'starting' ||
    quiz.status === 'processing'

  return (
    <>
      <Nav showSectionLinks={false} />

      <div className="studio">
        <div className="studio-head">
          <div className="section-tag">Studio · Generate</div>
          <div className="section-heading">Build your<br />study set</div>
          <div className="section-sub">
            — Upload your notes, then choose what to generate
          </div>
        </div>

        {/* STEP 01 — UPLOAD */}
        <div className="panel">
          <div className="panel-bar">
            <div className="panel-title">01 · Input · Upload material</div>
            <div className="panel-status">
              {doc ? 'LOADED' : uploading ? 'UPLOADING' : 'AWAITING_FILE'}
            </div>
          </div>
          <div className="panel-body">
            {!doc ? (
              <FileUpload onFile={handleFile} disabled={uploading} />
            ) : (
              <div className="doc-row">
                <div className="doc-meta">
                  <span className="dropzone-icon" style={{ margin: 0, fontSize: '1.2rem' }}>
                    📄
                  </span>
                  <div style={{ minWidth: 0 }}>
                    <div className="doc-file">{doc.fileName}</div>
                    <div className="doc-id">DOC_ID: {doc.id}</div>
                  </div>
                </div>
                <span className="doc-badge">PROCESSED · OK</span>
              </div>
            )}
            {uploadError && <div className="error-line">// ERROR: {uploadError}</div>}

            {doc && (
              <button
                className="btn ghost"
                style={{ marginTop: '20px' }}
                onClick={() => {
                  setDoc(null)
                  setUploadError('')
                  flashcards.reset()
                  quiz.reset()
                }}
                disabled={busy}
              >
                Upload different file
              </button>
            )}
          </div>
        </div>

        {/* STEP 02 — CHOOSE WHAT TO GENERATE */}
        {doc && (
          <>
            <div className="studio-actions">
              <button
                className="btn primary"
                onClick={() => flashcards.start(doc.id)}
                disabled={
                  flashcards.status === 'starting' ||
                  flashcards.status === 'processing'
                }
              >
                {flashcards.status === 'completed'
                  ? 'Regenerate flashcards'
                  : 'Generate flashcards'}
              </button>
              <button
                className="btn ghost"
                onClick={() => quiz.start(doc.id)}
                disabled={
                  quiz.status === 'starting' || quiz.status === 'processing'
                }
              >
                {quiz.status === 'completed' ? 'Regenerate quiz' : 'Generate quiz'}
              </button>
            </div>

            {/* STEP 03 — RESULTS */}
            <div className="gen-grid">
              <GenerationPanel
                title="02 · Flashcards"
                job={flashcards}
                emptyHint="// NO_DECK_YET — run 'Generate flashcards' to build a 10-card deck. Click any card to flip."
              >
                <FlashcardDeck cards={flashcards.result} />
              </GenerationPanel>

              <GenerationPanel
                title="03 · Quiz"
                job={quiz}
                emptyHint="// NO_QUIZ_YET — run 'Generate quiz' for 5 multiple-choice questions. Pick an answer to check it."
              >
                <QuizView quiz={quiz.result} />
              </GenerationPanel>
            </div>
          </>
        )}
      </div>

      <Footer />
    </>
  )
}
