import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import ErrorBoundary from './components/ErrorBoundary.jsx'
import { AuthProvider } from './context/AuthContext.jsx'
import { JobsProvider } from './context/JobsContext.jsx'
import './styles/theme.css'

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <JobsProvider>
          <ErrorBoundary>
            <App />
          </ErrorBoundary>
        </JobsProvider>
      </AuthProvider>
    </BrowserRouter>
  </React.StrictMode>,
)
