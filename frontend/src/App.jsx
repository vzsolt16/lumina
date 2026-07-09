import { Routes, Route, Navigate, useLocation } from 'react-router-dom'
import DataStream from './components/DataStream.jsx'
import ProtectedRoute from './components/ProtectedRoute.jsx'
import Landing from './pages/Landing.jsx'
import Studio from './pages/Studio.jsx'
import StudioDocument from './pages/StudioDocument.jsx'
import ContentTab from './pages/document/ContentTab.jsx'
import FlashcardsTab from './pages/document/FlashcardsTab.jsx'
import QuizTab from './pages/document/QuizTab.jsx'
import ChatTab from './pages/document/ChatTab.jsx'
import Login from './pages/Login.jsx'
import Register from './pages/Register.jsx'

export default function App() {
  // Landing + auth run their own full-bleed "sleeve" chrome; the data-stream
  // sidebar and its wrapper offset belong to the studio surfaces only.
  const { pathname } = useLocation()
  const isSleeve = pathname === '/' || pathname === '/login' || pathname === '/register'

  return (
    <>
      {!isSleeve && <DataStream />}
      <div className={isSleeve ? undefined : 'wrapper'}>
        <Routes>
          <Route path="/" element={<Landing />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route
            path="/studio"
            element={
              <ProtectedRoute>
                <Studio />
              </ProtectedRoute>
            }
          />
          <Route
            path="/studio/:docId"
            element={
              <ProtectedRoute>
                <StudioDocument />
              </ProtectedRoute>
            }
          >
            <Route index element={<Navigate to="flashcards" replace />} />
            <Route path="content" element={<ContentTab />} />
            <Route path="flashcards" element={<FlashcardsTab />} />
            <Route path="quiz" element={<QuizTab />} />
            <Route path="chat" element={<ChatTab />} />
          </Route>
        </Routes>
      </div>
    </>
  )
}
