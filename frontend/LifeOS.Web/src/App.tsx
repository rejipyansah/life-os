import { useState, useEffect } from 'react'
import './App.css'

function App() {
  const [backendStatus, setBackendStatus] = useState<string>('Checking...')

  useEffect(() => {
    fetch('/api/health')
      .then((res) => {
        if (!res.ok) throw new Error('Not ok')
        return res.json()
      })
      .then((data) => {
        if (data.status === 'ok') {
          setBackendStatus('Backend: Connected')
        } else {
          setBackendStatus('Backend: Disconnected')
        }
      })
      .catch(() => {
        setBackendStatus('Backend: Disconnected')
      })
  }, [])

  return (
    <div className="app">
      <h1>Life OS</h1>
      <p>{backendStatus}</p>
    </div>
  )
}

export default App
