// Install-to-home-screen support: device detection, the captured Chromium
// install prompt, and the per-browser "asked already" state.

export type InstallPlatform =
  | 'ios-safari'
  | 'ios-other'
  | 'android-chrome'
  | 'android-samsung'
  | 'android-firefox'
  | 'android-other'

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

let deferred: BeforeInstallPromptEvent | null = null
const listeners = new Set<() => void>()
const emit = () => listeners.forEach((l) => l())

// Registered at module load so an early beforeinstallprompt is not missed.
if (typeof window !== 'undefined') {
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault()
    deferred = e as BeforeInstallPromptEvent
    emit()
  })
  window.addEventListener('appinstalled', () => {
    deferred = null
    writeState('installed')
    emit()
  })
}

export function subscribeInstall(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function canPromptInstall(): boolean {
  return deferred !== null
}

// Shows the native install dialog (Chromium only). Resolves true if accepted.
export async function promptInstall(): Promise<boolean> {
  if (!deferred) return false
  const evt = deferred
  deferred = null
  emit()
  await evt.prompt()
  const { outcome } = await evt.userChoice
  if (outcome === 'accepted') writeState('installed')
  return outcome === 'accepted'
}

export function isStandalone(): boolean {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    window.matchMedia('(display-mode: fullscreen)').matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  )
}

// Phones and tablets only; null on desktop.
export function detectPlatform(): InstallPlatform | null {
  const ua = navigator.userAgent
  // iPadOS reports a Mac user agent; touch support gives it away.
  const iOS = /iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)
  if (iOS) {
    return /CriOS|FxiOS|EdgiOS|OPiOS|GSA\//.test(ua) ? 'ios-other' : 'ios-safari'
  }
  if (/Android/.test(ua)) {
    if (/SamsungBrowser/.test(ua)) return 'android-samsung'
    if (/Firefox/.test(ua)) return 'android-firefox'
    if (/Chrome/.test(ua) && !/EdgA|OPR|Opera/.test(ua)) return 'android-chrome'
    return 'android-other'
  }
  return null
}

const KEY = 'bl.install.v1'
const SNOOZE_MS = 14 * 24 * 60 * 60 * 1000

type StoredState = 'installed' | 'never' | `snoozed:${number}`

function readState(): string | null {
  try {
    return localStorage.getItem(KEY)
  } catch {
    return null
  }
}

function writeState(value: StoredState) {
  try {
    localStorage.setItem(KEY, value)
  } catch {
    // Storage blocked; the prompt may offer itself again next visit.
  }
}

export function snoozeInstall() {
  writeState(`snoozed:${Date.now()}`)
}

export function neverAskInstall() {
  writeState('never')
}

// True when this device should be offered the install guide now.
export function shouldOfferInstall(): boolean {
  if (isStandalone() || detectPlatform() === null) return false
  const state = readState()
  if (state === 'installed' || state === 'never') return false
  if (state?.startsWith('snoozed:')) {
    const at = Number(state.slice('snoozed:'.length))
    if (Number.isFinite(at) && Date.now() - at < SNOOZE_MS) return false
  }
  return true
}
