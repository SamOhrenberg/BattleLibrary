import { useEffect, useId, useRef, useState, useSyncExternalStore, type ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'
import {
  canPromptInstall,
  detectPlatform,
  neverAskInstall,
  promptInstall,
  shouldOfferInstall,
  snoozeInstall,
  subscribeInstall,
  type InstallPlatform,
} from '../lib/install'
import { useTour } from './TourContext'
import './install.css'

const Glyph = ({ children }: { children: ReactNode }) => (
  <span className="install-glyph" aria-hidden="true">
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
      {children}
    </svg>
  </span>
)

const ShareIcon = () => (
  <Glyph>
    <path d="M12 3v12M8 7l4-4 4 4" />
    <path d="M6 11v8a2 2 0 002 2h8a2 2 0 002-2v-8" />
  </Glyph>
)

const DotsIcon = () => (
  <Glyph>
    <circle cx="12" cy="5" r="1.2" fill="currentColor" />
    <circle cx="12" cy="12" r="1.2" fill="currentColor" />
    <circle cx="12" cy="19" r="1.2" fill="currentColor" />
  </Glyph>
)

const MenuIcon = () => (
  <Glyph>
    <path d="M4 7h16M4 12h16M4 17h16" />
  </Glyph>
)

const AddIcon = () => (
  <Glyph>
    <rect x="4" y="4" width="16" height="16" rx="4" />
    <path d="M12 8v8M8 12h8" />
  </Glyph>
)

const STEPS: Record<InstallPlatform, { browser: string; steps: ReactNode[] }> = {
  'ios-safari': {
    browser: 'Safari on iPhone and iPad',
    steps: [
      <>Tap <ShareIcon /> <b>Share</b> in the toolbar. On newer iOS it sits inside the <b>•••</b> menu.</>,
      <>Scroll down and tap <AddIcon /> <b>Add to Home Screen</b>.</>,
      <>Leave <b>Open as Web App</b> on, then tap <b>Add</b>.</>,
    ],
  },
  'ios-other': {
    browser: 'your iPhone browser',
    steps: [
      <>Tap <ShareIcon /> <b>Share</b> (in Chrome it is next to the address bar).</>,
      <>Tap <AddIcon /> <b>Add to Home Screen</b>. You may need to tap <b>More</b> first.</>,
      <>Not listed? Open this page in <b>Safari</b> and add it from there.</>,
    ],
  },
  'android-chrome': {
    browser: 'Chrome on Android',
    steps: [
      <>Tap <DotsIcon /> the menu in the top-right corner.</>,
      <>Tap <AddIcon /> <b>Add to Home screen</b>, then <b>Install</b>.</>,
      <>Confirm, and the app appears in your app drawer.</>,
    ],
  },
  'android-samsung': {
    browser: 'Samsung Internet',
    steps: [
      <>Tap <MenuIcon /> the menu in the bottom toolbar.</>,
      <>Tap <AddIcon /> <b>Add page to</b>, then <b>Home screen</b>.</>,
      <>Tap <b>Add</b> to confirm.</>,
    ],
  },
  'android-firefox': {
    browser: 'Firefox on Android',
    steps: [
      <>Tap <DotsIcon /> the menu.</>,
      <>Tap <AddIcon /> <b>Add app to Home screen</b>.</>,
      <>Tap <b>Add</b> to confirm.</>,
    ],
  },
  'android-other': {
    browser: 'your browser',
    steps: [
      <>Open your browser's <DotsIcon /> menu.</>,
      <>Look for <AddIcon /> <b>Install app</b> or <b>Add to Home screen</b>.</>,
      <>Not there? Open this page in <b>Chrome</b> and install from there.</>,
    ],
  },
}

const BENEFITS: { title: string; body: string; icon: ReactNode }[] = [
  {
    title: 'Full screen at the table',
    body: 'No address bar or tabs, just your list and the game.',
    icon: (
      <path d="M4 9V5a1 1 0 011-1h4M15 4h4a1 1 0 011 1v4M20 15v4a1 1 0 01-1 1h-4M9 20H5a1 1 0 01-1-1v-4" />
    ),
  },
  {
    title: 'Opens instantly',
    body: 'Launch from your home screen like any other app.',
    icon: <path d="M13 3L5 14h6l-1 7 8-11h-6l1-7z" />,
  },
  {
    title: 'Works on bad signal',
    body: 'Datasheets you have opened stay readable when the store Wi-Fi drops.',
    icon: (
      <>
        <path d="M2 9a15 15 0 0120 0M5 12.5a10 10 0 0114 0M8.5 16a5 5 0 017 0" />
        <circle cx="12" cy="19.5" r="1" fill="currentColor" />
      </>
    ),
  },
  {
    title: 'Always up to date',
    body: 'New features arrive automatically. Nothing to download from a store.',
    icon: <path d="M20 11a8 8 0 10-2.3 5.7M20 4v7h-7" />,
  },
]

// Shown once after sign-in on phones and tablets that are not already running the installed app.
export function InstallPrompt() {
  const { status } = useAuth()
  const tour = useTour()
  const [open, setOpen] = useState(false)
  const [platform] = useState(detectPlatform)
  const canPrompt = useSyncExternalStore(subscribeInstall, canPromptInstall)
  const titleId = useId()
  const sheetRef = useRef<HTMLDivElement>(null)

  // Waits for the onboarding tour, and yields to it if it starts while open.
  const visible = open && status === 'authenticated' && !tour.active

  useEffect(() => {
    if (status !== 'authenticated' || tour.active || !shouldOfferInstall()) return
    const t = window.setTimeout(() => setOpen(true), 1200)
    return () => window.clearTimeout(t)
  }, [status, tour.active])

  useEffect(() => {
    if (!visible) return
    sheetRef.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        snoozeInstall()
        setOpen(false)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [visible])

  if (!visible || !platform) return null

  const guide = STEPS[platform]

  function later() {
    snoozeInstall()
    setOpen(false)
  }

  function never() {
    neverAskInstall()
    setOpen(false)
  }

  async function install() {
    const accepted = await promptInstall()
    if (accepted) setOpen(false)
  }

  return (
    <div className="install-backdrop" onClick={later}>
      <div
        ref={sheetRef}
        tabIndex={-1}
        className="install-sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="install-grip" aria-hidden="true" />
        <button type="button" className="install-close" aria-label="Close" onClick={later}>
          ×
        </button>

        <header className="install-hero">
          <div className="install-icon-wrap">
            <img src={`${import.meta.env.BASE_URL}pwa-192x192.png`} alt="" width={72} height={72} />
          </div>
          <p className="install-eyebrow">Get the app</p>
          <h2 id={titleId}>Put the Battle Library on your home screen</h2>
          <p className="install-lede">
            It installs straight from your browser in a few seconds. No app store, no download.
          </p>
        </header>

        <ul className="install-benefits">
          {BENEFITS.map((b) => (
            <li key={b.title}>
              <span className="install-benefit-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                  {b.icon}
                </svg>
              </span>
              <span>
                <b>{b.title}</b>
                <span className="install-benefit-body">{b.body}</span>
              </span>
            </li>
          ))}
        </ul>

        {canPrompt ? (
          <p className="install-onetap">Your browser supports one-tap install.</p>
        ) : (
          <section className="install-steps" aria-label="How to install">
            <p className="install-steps-title">How to install in {guide.browser}</p>
            <ol>
              {guide.steps.map((s, i) => (
                <li key={i}>
                  <span className="install-step-n" aria-hidden="true">{i + 1}</span>
                  <span className="install-step-text">{s}</span>
                </li>
              ))}
            </ol>
          </section>
        )}

        <div className="install-actions">
          {canPrompt ? (
            <button type="button" className="install-btn primary" onClick={() => void install()}>
              Install the app
            </button>
          ) : (
            <button type="button" className="install-btn primary" onClick={later}>
              Got it
            </button>
          )}
          <div className="install-secondary">
            <button type="button" className="install-link" onClick={later}>
              Maybe later
            </button>
            <span aria-hidden="true">·</span>
            <button type="button" className="install-link" onClick={never}>
              Don't ask again
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
