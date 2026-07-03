import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext.jsx'
import './Landing.css'
import './Auth.css'

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = location.state?.from?.pathname || '/studio'

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      await login(email, password)
      navigate(from, { replace: true })
    } catch (err) {
      setError(err?.message || 'Login failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="sleeve sleeve--auth">
      <header className="sl-nav">
        <Link className="sl-nav__brand" to="/">
          <span className="sl-nav__logo">lumina</span>
          <span className="tag tag--dim">lmn·001</span>
        </Link>
        <span className="sl-nav__cat tag tag--dim">study sessions</span>
        <Link className="sl-nav__play" to="/register">sign up</Link>
      </header>

      <main className="auth-stage">
        <div className="auth-stage__pane" aria-hidden="true" />
        <div className="auth-stage__corner" aria-hidden="true">
          stereo · 44.1&nbsp;khz<br />limited edition
        </div>

        <form className="auth-sleeve" onSubmit={handleSubmit}>
          <i aria-hidden="true" /><i aria-hidden="true" /><i aria-hidden="true" /><i aria-hidden="true" />
          <div className="rule-tag rule-tag--left auth-kick"><span className="tag">resume session</span></div>
          <h1 className="auth-title">sign in</h1>
          <p className="auth-sub">pick up where the last session faded out.</p>

          {error && <div className="auth-error" role="alert">error — {error}</div>}

          <div className="auth-field">
            <label className="auth-label" htmlFor="email">email</label>
            <input
              id="email"
              className="auth-input"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          </div>

          <div className="auth-field">
            <label className="auth-label" htmlFor="password">password</label>
            <input
              id="password"
              className="auth-input"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </div>

          <button className="btn btn--solid auth-submit" type="submit" disabled={submitting}>
            <span className="tri" aria-hidden="true" />
            {submitting ? 'signing in…' : 'sign in'}
          </button>

          <div className="auth-switch">
            no account yet? <Link to="/register">start your first session</Link>
          </div>
        </form>
      </main>

      <div className="fx-scan" aria-hidden="true" />
      <div className="fx-grain" aria-hidden="true" />
    </div>
  )
}
