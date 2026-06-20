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

/**
 * Upload a .txt or .md file.
 * @returns {Promise<{ id: string, fileName: string }>}
 */
export function uploadDocument(file) {
  const form = new FormData()
  form.append('file', file)
  return request('/api/documents', { method: 'POST', body: form })
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
