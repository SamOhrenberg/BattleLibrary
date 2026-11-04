import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './styles/theme.css'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'
import { TourProvider } from './onboarding/TourContext'
import { ToastProvider } from './ui/Toast'
import { initTheme } from './styles/theme'
import { ErrorBoundary } from './ui/ErrorBoundary'

// Apply the saved (or default) theme before the first paint.
initTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
    <AuthProvider>
      <ToastProvider>
        <TourProvider>
          <App />
        </TourProvider>
      </ToastProvider>
    </AuthProvider>
    </ErrorBoundary>
  </StrictMode>,
)
