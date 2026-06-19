import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../context/AuthContext.jsx'

// Gate for authenticated-only routes. While the initial silent refresh is in
// flight we show a placeholder so we don't bounce a logged-in user to /login
// on a hard reload.
export default function ProtectedRoute({ children }) {
  const { user, initializing } = useAuth()
  const location = useLocation()

  if (initializing) {
    return <div className="auth-booting">// AUTHENTICATING…</div>
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return children
}
