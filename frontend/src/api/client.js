// Thin fetch wrapper around the Lumina backend.
// All requests go through Vite's dev proxy (/api -> https://localhost:5131),
// so paths here are origin-relative.

async function handle(res) {
  if (!res.ok) {
    let detail = ''
    try {
      detail = await res.text()
    } catch {
      /* ignore */
    }
    throw new Error(detail || `Request failed (${res.status})`)
  }
  if (res.status === 204) return null
  const text = await res.text()
  return text ? JSON.parse(text) : null
}

/**
 * Upload a .txt or .md file.
 * @returns {Promise<{ id: string, fileName: string }>}
 */
export async function uploadDocument(file) {
  const form = new FormData()
  form.append('file', file)
  const res = await fetch('/api/documents', { method: 'POST', body: form })
  return handle(res)
}

/**
 * Kick off async flashcard generation for a document.
 * @returns {Promise<{ flashcardJobId: string, status: string }>}
 */
export async function generateFlashcards(documentId) {
  const res = await fetch(`/api/documents/${documentId}/flashcards`, {
    method: 'POST',
  })
  return handle(res)
}

/**
 * Kick off async quiz generation for a document.
 * @returns {Promise<{ quizId: string, status: string }>}
 */
export async function generateQuiz(documentId) {
  const res = await fetch(`/api/documents/${documentId}/quizzes`, {
    method: 'POST',
  })
  return handle(res)
}

/** Fetch all persisted flashcards for a document (fallback / re-fetch). */
export async function getFlashcards(documentId) {
  const res = await fetch(`/api/documents/${documentId}/flashcards`)
  return handle(res)
}

/** Fetch all persisted quizzes for a document (fallback / re-fetch). */
export async function getQuizzes(documentId) {
  const res = await fetch(`/api/documents/${documentId}/quizzes`)
  return handle(res)
}
