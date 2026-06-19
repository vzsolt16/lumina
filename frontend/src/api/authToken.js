// In-memory access token store. The access token is intentionally NOT persisted
// to localStorage/sessionStorage — it lives only in this module variable, so it
// vanishes on reload (a fresh one is obtained via the HttpOnly refresh cookie).

let accessToken = null
let onAuthExpired = null

export function getAccessToken() {
  return accessToken
}

export function setAccessToken(token) {
  accessToken = token
}

export function clearAccessToken() {
  accessToken = null
}

// Registered by AuthContext so the API client can tell it when a silent refresh
// has failed and the session is effectively over.
export function setOnAuthExpired(callback) {
  onAuthExpired = callback
}

export function notifyAuthExpired() {
  onAuthExpired?.()
}
