import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext.jsx'
import './Landing.css'
import './Auth.css'

export default function Register() {
  const { register } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      await register(email, password)
      navigate('/studio', { replace: true })
    } catch (err) {
      setError(err?.message || 'Registration failed.')
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
        <Link className="sl-nav__play" to="/login">sign in</Link>
      </header>

      <main className="auth-stage">
        <div className="auth-stage__pane" aria-hidden="true" />
        <div className="auth-stage__corner" aria-hidden="true">
          stereo · 44.1&nbsp;khz<br />limited edition
        </div>

        <form className="auth-sleeve" onSubmit={handleSubmit}>
          <i aria-hidden="true" /><i aria-hidden="true" /><i aria-hidden="true" /><i aria-hidden="true" />
          <div className="rule-tag rule-tag--left auth-kick"><span className="tag">new session</span></div>
          <h1 className="auth-title">create account</h1>
          <p className="auth-sub">set up your account and put your notes on repeat.</p>

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
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              minLength={8}
            />
          </div>
          <div className="auth-hint">
            min 8 chars · 1 uppercase · 1 lowercase · 1 digit
          </div>

          <button className="btn btn--solid auth-submit" type="submit" disabled={submitting}>
            <span className="tri" aria-hidden="true" />
            {submitting ? 'creating…' : 'begin session'}
          </button>

          <div className="auth-switch">
            already have an account? <Link to="/login">sign in</Link>
          </div>
        </form>
      </main>

      <div className="fx-scan" aria-hidden="true" />
      <div className="fx-grain" aria-hidden="true" />
    </div>
  )
}
