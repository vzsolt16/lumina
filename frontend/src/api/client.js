// Thin fetch wrapper around the Lumina backend.
// All requests go through Vite's dev proxy (/api -> http://localhost:5131),
// so paths here are origin-relative.
//
// Auth model: the access token lives in memory (authToken.js) and is attached
// as a Bearer header. The refresh token is an HttpOnly cookie sent automatically
// with credentials:'include'. On a 401 we transparently try to refresh the
// access token once and replay the request.

import {
  getAccessToken,
  setAccessToken,
  clearAccessToken,
  notifyAuthExpired,
} from './authToken.js'

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

// Shared in-flight refresh so concurrent 401s trigger only one refresh call.
let refreshInFlight = null

function refreshAccessToken() {
  if (!refreshInFlight) {
    refreshInFlight = fetch('/api/auth/refresh', {
      method: 'POST',
      credentials: 'include',
    })
      .then(async (res) => {
        if (!res.ok) return null
        const data = await res.json()
        setAccessToken(data.accessToken)
        return data
      })
      .catch(() => null)
      .finally(() => {
        refreshInFlight = null
      })
  }
  return refreshInFlight
}

/**
 * Core request helper.
 * @param {string} path
 * @param {object} [opts] - fetch options plus { auth, retry } flags.
 */
async function request(path, { auth = true, retry = true, ...options } = {}) {
  const headers = new Headers(options.headers || {})
  const token = getAccessToken()
  if (auth && token) headers.set('Authorization', `Bearer ${token}`)

  const res = await fetch(path, { ...options, headers, credentials: 'include' })

  if (res.status === 401 && auth && retry) {
    const refreshed = await refreshAccessToken()
    if (refreshed) {
      return request(path, { ...options, auth, retry: false })
    }
    clearAccessToken()
    notifyAuthExpired()
  }

  return handle(res)
}

const jsonHeaders = { 'Content-Type': 'application/json' }

/* ─── Auth ─────────────────────────────────────────────────────────── */

/** Register a new account; auto-logs in and returns the auth payload. */
export async function register(email, password) {
  const data = await request('/api/auth/register', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ email, password }),
    auth: false,
    retry: false,
  })
  setAccessToken(data.accessToken)
  return data
}

/** Log in with email + password. */
export async function login(email, password) {
  const data = await request('/api/auth/login', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ email, password }),
    auth: false,
    retry: false,
  })
  setAccessToken(data.accessToken)
  return data
}

/** Revoke the refresh token server-side and clear the access token locally. */
export async function logout() {
  try {
    await request('/api/auth/logout', { method: 'POST', auth: false, retry: false })
  } finally {
    clearAccessToken()
  }
}

/**
 * Attempt to restore a session from the refresh cookie (used on app load).
 * @returns {Promise<{ accessToken, userId, email } | null>}
 */
export function refreshSession() {
  return refreshAccessToken()
}

/* ─── Documents / generation ───────────────────────────────────────── */

/**
 * List the current user's documents (newest-first ordering is up to the caller).
 * @returns {Promise<Array<{ id: string, fileName: string, uploadedAt: string }>>}
 */
export function getDocuments() {
  return request('/api/documents')
}

/** Fetch a single document by id. */
export function getDocument(id) {
  return request(`/api/documents/${id}`)
}

/** Delete a document (and its flashcards, quizzes and chat) by id. */
export function deleteDocument(id) {
  return request(`/api/documents/${id}`, { method: 'DELETE' })
}

/**
 * Update a document's title and/or content. Send only the fields you want to
 * change — `{ fileName }` for a rename, `{ fileName, content }` for a full edit.
 * @param {string} id
 * @param {{ fileName?: string, content?: string }} payload
 * @returns {Promise<{ id, fileName, fileSize, uploadedAt, updatedAt, content }>}
 */
export function updateDocument(id, payload) {
  return request(`/api/documents/${id}`, {
    method: 'PATCH',
    headers: jsonHeaders,
    body: JSON.stringify(payload),
  })
}

/**
 * Move a document into a folder, or to the root with folderId = null.
 * @param {string} id
 * @param {string|null} folderId
 */
export function moveDocument(id, folderId) {
  return request(`/api/documents/${id}/folder`, {
    method: 'PATCH',
    headers: jsonHeaders,
    body: JSON.stringify({ folderId }),
  })
}

/**
 * Upload a .txt or .md file, optionally into a folder.
 * @param {File} file
 * @param {string|null} [folderId] - target folder, or null/undefined for the root.
 * @returns {Promise<{ id: string, fileName: string }>}
 */
export function uploadDocument(file, folderId = null) {
  const form = new FormData()
  form.append('file', file)
  if (folderId) form.append('folderId', folderId)
  return request('/api/documents', { method: 'POST', body: form })
}

/* ─── Folders ──────────────────────────────────────────────────────── */

/**
 * List all of the current user's folders (flat; the client builds the tree).
 * @returns {Promise<Array<{ id, name, parentId: string|null, createdAt }>>}
 */
export function getFolders() {
  return request('/api/folders')
}

/** Create a folder. parentId null = root level. */
export function createFolder(name, parentId = null) {
  return request('/api/folders', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ name, parentId }),
  })
}

/** Rename a folder (no duplicate names on the same level). */
export function renameFolder(id, name) {
  return request(`/api/folders/${id}`, {
    method: 'PATCH',
    headers: jsonHeaders,
    body: JSON.stringify({ name }),
  })
}

/** Move a folder under a new parent, or to the root with parentId = null. */
export function moveFolder(id, parentId) {
  return request(`/api/folders/${id}/move`, {
    method: 'PATCH',
    headers: jsonHeaders,
    body: JSON.stringify({ parentId }),
  })
}

/** Delete a folder and everything inside it (cascade). */
export function deleteFolder(id) {
  return request(`/api/folders/${id}`, { method: 'DELETE' })
}

/**
 * Kick off async flashcard generation for a document.
 * @returns {Promise<{ flashcardJobId: string, status: string }>}
 */
export function generateFlashcards(documentId) {
  return request(`/api/documents/${documentId}/flashcards`, { method: 'POST' })
}

/**
 * Kick off async quiz generation for a document.
 * @returns {Promise<{ quizId: string, status: string }>}
 */
export function generateQuiz(documentId) {
  return request(`/api/documents/${documentId}/quizzes`, { method: 'POST' })
}

/** Fetch all persisted flashcards for a document (fallback / re-fetch). */
export function getFlashcards(documentId) {
  return request(`/api/documents/${documentId}/flashcards`)
}

/** Fetch all persisted quizzes for a document (fallback / re-fetch). */
export function getQuizzes(documentId) {
  return request(`/api/documents/${documentId}/quizzes`)
}

/* ─── Chat conversations ───────────────────────────────────────────── */

/**
 * List a document's chat conversations, most-recently-updated first.
 * @returns {Promise<Array<{ id, title, createdAt, updatedAt }>>}
 */
export function getConversations(documentId) {
  return request(`/api/documents/${documentId}/chat/conversations`)
}

/**
 * Start a new, empty conversation for a document.
 * @returns {Promise<{ id, title, createdAt, updatedAt }>}
 */
export function createConversation(documentId) {
  return request(`/api/documents/${documentId}/chat/conversations`, {
    method: 'POST',
  })
}

/**
 * Fetch one conversation's messages, oldest-first.
 * Live answers stream over the /ws/chat hub, not this endpoint.
 * @returns {Promise<Array<{ id, role: 'user' | 'assistant', content, createdAt }>>}
 */
export function getConversationMessages(documentId, conversationId) {
  return request(
    `/api/documents/${documentId}/chat/conversations/${conversationId}/messages`,
  )
}

/** Delete a conversation (and its messages) by id. */
export function deleteConversation(documentId, conversationId) {
  return request(
    `/api/documents/${documentId}/chat/conversations/${conversationId}`,
    { method: 'DELETE' },
  )
}

/**
 * Apply a pending AI edit proposal to the document.
 * 409 = already resolved, or the document changed under the proposal.
 * @returns {Promise<{ id, fileName, fileSize, uploadedAt, updatedAt, content }>} the updated document
 */
export function applyEditProposal(documentId, conversationId, messageId) {
  return request(
    `/api/documents/${documentId}/chat/conversations/${conversationId}/messages/${messageId}/proposal/apply`,
    { method: 'POST' },
  )
}

/** Reject a pending AI edit proposal (leaves the document untouched). */
export function rejectEditProposal(documentId, conversationId, messageId) {
  return request(
    `/api/documents/${documentId}/chat/conversations/${conversationId}/messages/${messageId}/proposal/reject`,
    { method: 'POST' },
  )
}
