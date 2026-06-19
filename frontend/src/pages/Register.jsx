import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import Nav from '../components/Nav.jsx'
import { useAuth } from '../context/AuthContext.jsx'
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
    <>
      <Nav showSectionLinks={false} />
      <div className="auth-wrap">
        <form className="auth-card" onSubmit={handleSubmit}>
          <div className="section-tag">Access · New unit</div>
          <div className="section-heading">Create account</div>
          <div className="section-sub">— Spin up your own study workspace</div>

          {error && <div className="auth-error">// ERROR: {error}</div>}

          <div className="auth-field">
            <label className="auth-label" htmlFor="email">Email</label>
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
            <label className="auth-label" htmlFor="password">Password</label>
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

          <button className="btn primary auth-submit" type="submit" disabled={submitting}>
            {submitting ? 'Creating…' : 'Create account'}
          </button>

          <div className="auth-switch">
            Already have an account? <Link to="/login">Log in</Link>
          </div>
        </form>
      </div>
    </>
  )
}
