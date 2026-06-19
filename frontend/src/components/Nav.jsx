import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext.jsx'

// Sticky nav from landing-genx.html. On the landing page the section links
// (#features etc.) scroll; elsewhere they route home first.
export default function Nav({ showSectionLinks = true }) {
  const navigate = useNavigate()
  const { user, logout } = useAuth()

  async function handleLogout() {
    await logout()
    navigate('/')
  }

  return (
    <nav>
      <Link to="/" className="nav-logo">
        LUMINA
        <span className="logo-mark">Study Systems</span>
      </Link>
      {showSectionLinks && (
        <ul className="nav-links">
          <li><a href="#features">Features</a></li>
          <li><a href="#how">Protocol</a></li>
          <li><a href="#companion">AI Unit</a></li>
        </ul>
      )}
      <div className="nav-cta">
        {user ? (
          <>
            <button className="btn-nav ghost" onClick={() => navigate('/studio')}>
              Studio
            </button>
            <button className="btn-nav solid" onClick={handleLogout}>
              Log out
            </button>
          </>
        ) : (
          <>
            <button className="btn-nav ghost" onClick={() => navigate('/login')}>
              Log in
            </button>
            <button className="btn-nav solid" onClick={() => navigate('/register')}>
              Access free
            </button>
          </>
        )}
      </div>
    </nav>
  )
}
