import { createContext, useContext, useEffect, useState } from 'react'
import {
  login as apiLogin,
  register as apiRegister,
  logout as apiLogout,
  refreshSession,
} from '../api/client.js'
import { setOnAuthExpired } from '../api/authToken.js'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null) // { id, email } | null
  const [initializing, setInitializing] = useState(true)

  useEffect(() => {
    // If a background request's silent refresh fails, drop the user.
    setOnAuthExpired(() => setUser(null))

    // On load, try to restore a session from the refresh cookie.
    let cancelled = false
    refreshSession()
      .then((data) => {
        if (!cancelled && data) {
          setUser({ id: data.userId, email: data.email })
        }
      })
      .finally(() => {
        if (!cancelled) setInitializing(false)
      })

    return () => {
      cancelled = true
      setOnAuthExpired(null)
    }
  }, [])

  async function login(email, password) {
    const data = await apiLogin(email, password)
    setUser({ id: data.userId, email: data.email })
    return data
  }

  async function register(email, password) {
    const data = await apiRegister(email, password)
    setUser({ id: data.userId, email: data.email })
    return data
  }

  async function logout() {
    await apiLogout()
    setUser(null)
  }

  const value = { user, initializing, login, register, logout }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
