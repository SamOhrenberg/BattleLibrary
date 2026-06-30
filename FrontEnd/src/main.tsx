import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'
import { TourProvider } from './onboarding/TourContext'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider>
      <TourProvider>
        <App />
      </TourProvider>
    </AuthProvider>
  </StrictMode>,
)
