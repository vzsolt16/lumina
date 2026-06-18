import { Routes, Route } from 'react-router-dom'
import DataStream from './components/DataStream.jsx'
import Landing from './pages/Landing.jsx'
import Studio from './pages/Studio.jsx'

export default function App() {
  return (
    <>
      <DataStream />
      <div className="wrapper">
        <Routes>
          <Route path="/" element={<Landing />} />
          <Route path="/studio" element={<Studio />} />
        </Routes>
      </div>
    </>
  )
}
