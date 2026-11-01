import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import './toast.css'

type ToastKind = 'info' | 'success' | 'error'

interface Toast {
  id: number
  message: string
  kind: ToastKind
}

interface ToastApi {
  push: (message: string, kind?: ToastKind) => void
}

const ToastContext = createContext<ToastApi | null>(null)

// Transient, auto-dismissing messages. Errors linger longer than confirmations.
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const seq = useRef(0)

  const remove = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  const push = useCallback(
    (message: string, kind: ToastKind = 'info') => {
      const id = ++seq.current
      setToasts((prev) => [...prev, { id, message, kind }])
      window.setTimeout(() => remove(id), kind === 'error' ? 7000 : 4000)
    },
    [remove],
  )

  const api = useMemo(() => ({ push }), [push])

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="toast-host" role="status" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`toast ${t.kind}`}>
            <span className="toast-msg">{t.message}</span>
            <button type="button" className="toast-x" aria-label="Dismiss" onClick={() => remove(t.id)}>
              ×
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) {
    throw new Error('useToast must be used within a ToastProvider')
  }
  return ctx
}
