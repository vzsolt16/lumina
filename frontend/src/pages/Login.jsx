import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import Nav from '../components/Nav.jsx'
import { useAuth } from '../context/AuthContext.jsx'
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
    <>
      <Nav showSectionLinks={false} />
      <div className="auth-wrap">
        <form className="auth-card" onSubmit={handleSubmit}>
          <div className="section-tag">Access · Returning unit</div>
          <div className="section-heading">Log in</div>
          <div className="section-sub">— Resume your study systems</div>

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
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </div>

          <button className="btn primary auth-submit" type="submit" disabled={submitting}>
            {submitting ? 'Authenticating…' : 'Log in'}
          </button>

          <div className="auth-switch">
            No account yet? <Link to="/register">Create one</Link>
          </div>
        </form>
      </div>
    </>
  )
}
