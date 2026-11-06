import { useEffect, useState } from 'react'
import { useRegisterSW } from 'virtual:pwa-register/react'
import './toast.css'

// Update prompt for a new deploy, plus an offline notice. A prompt (not an
// auto-reload) so a live game is never swapped out mid-round.
export function PwaStatus() {
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW()
  const [online, setOnline] = useState(() => navigator.onLine)

  useEffect(() => {
    const on = () => setOnline(true)
    const off = () => setOnline(false)
    window.addEventListener('online', on)
    window.addEventListener('offline', off)
    return () => {
      window.removeEventListener('online', on)
      window.removeEventListener('offline', off)
    }
  }, [])

  if (!needRefresh && online) {
    return null
  }

  return (
    <div className="toast-host pwa-host" role="status" aria-live="polite">
      {!online && (
        <div className="toast error">
          <span className="toast-msg">You're offline. Changes won't save until you reconnect.</span>
        </div>
      )}
      {needRefresh && (
        <div className="toast">
          <span className="toast-msg">A new version is available.</span>
          <button type="button" className="btn-sm primary" onClick={() => void updateServiceWorker(true)}>
            Reload
          </button>
          <button type="button" className="toast-x" aria-label="Dismiss" onClick={() => setNeedRefresh(false)}>
            ×
          </button>
        </div>
      )}
    </div>
  )
}
