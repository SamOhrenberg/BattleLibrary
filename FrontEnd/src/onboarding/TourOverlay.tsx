import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useTour } from './TourContext'
import './tour.css'

type Placement = 'top' | 'bottom' | 'left' | 'right'

interface Box {
  top: number
  left: number
  width: number
  height: number
}

const PAD = 6
const GAP = 14
const MAX_CARD_W = 300

function cardWidth(): number {
  return Math.min(MAX_CARD_W, window.innerWidth - 24)
}

function findTarget(key: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-tour="${key}"]`)
}

// Clamp the tooltip to the viewport and pick a placement that actually fits.
// Side placements are skipped on narrow screens, so phones get top/bottom.
function place(box: Box, placement: Placement, cardW: number): { style: Box; arrow: Placement } {
  const vw = window.innerWidth
  const vh = window.innerHeight
  const order: Placement[] = [placement, 'bottom', 'top', 'right', 'left']

  for (const p of order) {
    if (p === 'bottom' && box.top + box.height + GAP + 120 > vh) continue
    if (p === 'top' && box.top - GAP - 120 < 0) continue
    if (p === 'right' && box.left + box.width + GAP + cardW > vw) continue
    if (p === 'left' && box.left - GAP - cardW < 0) continue
    return { style: anchor(box, p, vw, vh, cardW), arrow: p }
  }
  return { style: anchor(box, 'bottom', vw, vh, cardW), arrow: 'bottom' }
}

function anchor(box: Box, p: Placement, vw: number, vh: number, cardW: number): Box {
  const cx = box.left + box.width / 2
  const cy = box.top + box.height / 2
  let top = 0
  let left = 0
  if (p === 'bottom') {
    top = box.top + box.height + GAP
    left = cx - cardW / 2
  } else if (p === 'top') {
    top = box.top - GAP
    left = cx - cardW / 2
  } else if (p === 'right') {
    top = cy
    left = box.left + box.width + GAP
  } else {
    top = cy
    left = box.left - GAP - cardW
  }
  left = Math.max(12, Math.min(left, vw - cardW - 12))
  top = Math.max(12, Math.min(top, vh - 12))
  return { top, left, width: cardW, height: 0 }
}

export function TourOverlay() {
  const { active, step, stepIndex, steps, next, back, stop } = useTour()
  const [box, setBox] = useState<Box | null>(null)
  const [missing, setMissing] = useState(false)
  const rafRef = useRef<number | null>(null)

  // Scroll the current target into view when the step changes.
  useEffect(() => {
    if (!active || !step) return
    const el = findTarget(step.target)
    el?.scrollIntoView({ block: 'center', behavior: 'smooth' })
  }, [active, step, stepIndex])

  // Track the target's rect every frame while active - robust to the layout
  // shifts that adding units and opening panels cause.
  useLayoutEffect(() => {
    if (!active || !step) {
      setBox(null)
      return
    }
    let prev = ''
    const tick = () => {
      const el = findTarget(step.target)
      if (!el) {
        setMissing(true)
      } else {
        setMissing(false)
        const r = el.getBoundingClientRect()
        const nextBox: Box = {
          top: r.top - PAD,
          left: r.left - PAD,
          width: r.width + PAD * 2,
          height: r.height + PAD * 2,
        }
        const key = `${nextBox.top}|${nextBox.left}|${nextBox.width}|${nextBox.height}`
        if (key !== prev) {
          prev = key
          setBox(nextBox)
        }
      }
      rafRef.current = requestAnimationFrame(tick)
    }
    rafRef.current = requestAnimationFrame(tick)
    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current)
    }
  }, [active, step, stepIndex])

  if (!active || !step) return null

  const isLast = stepIndex === steps.length - 1
  const cardW = cardWidth()
  const placed = box ? place(box, step.placement ?? 'bottom', cardW) : null

  return (
    <div className="tour-root" role="dialog" aria-modal="false" aria-label="Guided tour">
      {box && (
        <div
          className="tour-spotlight"
          style={{ top: box.top, left: box.left, width: box.width, height: box.height }}
        />
      )}

      <div
        className={`tour-card tour-arrow-${placed?.arrow ?? 'bottom'}`}
        style={
          placed
            ? { top: placed.style.top, left: placed.style.left, width: cardW }
            : { top: 24, left: 24, width: cardW }
        }
      >
        <div className="tour-step-count">
          Step {stepIndex + 1} of {steps.length}
        </div>
        <h3 className="tour-title">{step.title}</h3>
        <p className="tour-body">{step.body}</p>
        {missing && (
          <p className="tour-waiting">Follow the highlighted step to continue…</p>
        )}
        <div className="tour-actions">
          <button type="button" className="tour-skip" onClick={() => stop(true)}>
            Skip
          </button>
          <div className="tour-nav">
            {stepIndex > 0 && (
              <button type="button" className="tour-btn" onClick={back}>
                Back
              </button>
            )}
            <button type="button" className="tour-btn tour-btn-primary" onClick={next}>
              {isLast ? 'Finish' : 'Next'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
