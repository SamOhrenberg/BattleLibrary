import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useTour, type TourInteraction } from './TourContext'
import './tour.css'

type Placement = 'top' | 'bottom' | 'left' | 'right'

const INPUT_SEL = 'input:not([type="hidden"]), textarea, select'
const CLICK_SEL = 'button, a[href], [role="button"]'

// Work out what the user must do with a step's target. An input wins over a
// button so a field wrapped in a clickable label still asks for typing.
function classify(el: HTMLElement): TourInteraction {
  if (el.matches(INPUT_SEL) || el.querySelector(INPUT_SEL)) return 'input'
  if (el.matches(CLICK_SEL) || el.querySelector(CLICK_SEL)) return 'click'
  return 'none'
}

// Can the target actually be typed into / changed? Guards against dead-ends
// like a disabled field or a single-option select, where 'input' is impossible.
function hasEditableControl(el: HTMLElement): boolean {
  const controls = el.matches(INPUT_SEL)
    ? [el]
    : Array.from(el.querySelectorAll<HTMLElement>(INPUT_SEL))
  return controls.some((c) => {
    const input = c as HTMLInputElement
    if (input.disabled || input.readOnly) return false
    if (c instanceof HTMLSelectElement) return c.options.length > 1
    return true
  })
}

interface Box {
  top: number
  left: number
  width: number
  height: number
}

const PAD = 6
const GAP = 14
const MAX_CARD_W = 300

// The visible part of the page, in document coordinates. On phones the
// on-screen keyboard shrinks and shifts this, so all positioning uses it.
interface View {
  top: number
  left: number
  width: number
  height: number
  keyboard: boolean
}

function getView(): View {
  const vv = window.visualViewport
  if (!vv) {
    return { top: window.scrollY, left: window.scrollX, width: window.innerWidth, height: window.innerHeight, keyboard: false }
  }
  return {
    top: vv.pageTop,
    left: vv.pageLeft,
    width: vv.width,
    height: vv.height,
    // A visual viewport well short of the layout viewport means the keyboard is up.
    keyboard: vv.height < window.innerHeight * 0.8,
  }
}

function cardWidth(view: View): number {
  return Math.min(MAX_CARD_W, view.width - 24)
}

function findTarget(key: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-tour="${key}"]`)
}

// Clamp the tooltip to the viewport and pick a placement that actually fits.
// Side placements are skipped on narrow screens, so phones get top/bottom.
// cardH is the measured card height, so tall cards near an edge switch sides
// instead of spilling off screen.
function place(
  box: Box,
  placement: Placement,
  cardW: number,
  cardH: number,
  vw: number,
  vh: number,
): { style: Box; arrow: Placement } {
  const order: Placement[] = [placement, 'bottom', 'top', 'right', 'left']

  for (const p of order) {
    if (p === 'bottom' && box.top + box.height + GAP + cardH > vh - 12) continue
    if (p === 'top' && box.top - GAP - cardH < 12) continue
    if (p === 'right' && box.left + box.width + GAP + cardW > vw) continue
    if (p === 'left' && box.left - GAP - cardW < 0) continue
    return { style: anchor(box, p, vw, vh, cardW, cardH), arrow: p }
  }
  // Nothing fits cleanly: fall back to bottom, clamped to stay on screen.
  return { style: anchor(box, 'bottom', vw, vh, cardW, cardH), arrow: 'bottom' }
}

function anchor(box: Box, p: Placement, vw: number, vh: number, cardW: number, cardH: number): Box {
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
  // Clamp vertically against the CSS transform each placement applies, so the
  // whole card stays on screen (top is shifted up a full height, the sides half).
  if (p === 'top') {
    top = Math.max(cardH + 12, Math.min(top, vh - 12))
  } else if (p === 'right' || p === 'left') {
    top = Math.max(cardH / 2 + 12, Math.min(top, vh - cardH / 2 - 12))
  } else {
    top = Math.max(12, Math.min(top, vh - cardH - 12))
  }
  return { top, left, width: cardW, height: 0 }
}

export function TourOverlay() {
  const { active, step, stepIndex, steps, next, back, stop, skipMissing } = useTour()
  const [box, setBox] = useState<Box | null>(null)
  const [view, setView] = useState<View>(getView)
  const [missing, setMissing] = useState(false)
  const [interaction, setInteraction] = useState<TourInteraction>('none')
  const [satisfied, setSatisfied] = useState(false)
  const [cardH, setCardH] = useState(0)
  const cardRef = useRef<HTMLDivElement | null>(null)
  const rafRef = useRef<number | null>(null)

  // Decide this step's interaction requirement from its target in the DOM
  // (overridable per step). The target may mount a frame or two late, so retry.
  useEffect(() => {
    if (!active || !step) {
      setInteraction('none')
      setSatisfied(false)
      return
    }
    setSatisfied(false)
    setInteraction(step.interact ?? 'none')
    let cancelled = false
    let tries = 0
    const resolve = () => {
      if (cancelled) return
      const el = findTarget(step.target)
      if (el) {
        const req = step.interact ?? classify(el)
        setInteraction(req)
        // Nothing to type into: let Next through so the step is not a dead-end.
        if (req === 'input' && !hasEditableControl(el)) setSatisfied(true)
        return
      }
      // Sections only some units have: pass over the step rather than stall on it.
      if (step.ifPresent && tries >= 12) {
        skipMissing()
        return
      }
      if (tries++ < 90) requestAnimationFrame(resolve)
    }
    resolve()
    return () => {
      cancelled = true
    }
  }, [active, step, stepIndex, skipMissing])

  // Watch for the required interaction on the target. Input steps unlock Next
  // once a value is entered; click steps advance on the press itself (unless a
  // milestone already drives the step, which would otherwise double-advance).
  useEffect(() => {
    if (!active || !step || interaction === 'none') return
    const handle = (e: Event) => {
      const el = findTarget(step.target)
      if (!el) return
      const t = e.target as Node
      if (el !== t && !el.contains(t)) return
      if (interaction === 'click') {
        if (e.type !== 'click' || step.advanceOn) return
        next()
      } else {
        if (e.type === 'change') {
          setSatisfied(true)
        } else {
          const value = (t as HTMLInputElement).value
          setSatisfied(typeof value === 'string' ? value.trim().length > 0 : true)
        }
      }
    }
    document.addEventListener('click', handle, true)
    document.addEventListener('input', handle, true)
    document.addEventListener('change', handle, true)
    return () => {
      document.removeEventListener('click', handle, true)
      document.removeEventListener('input', handle, true)
      document.removeEventListener('change', handle, true)
    }
  }, [active, step, stepIndex, interaction, next])

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
    let prevView = ''
    const tick = () => {
      const v = getView()
      const viewKey = `${v.top}|${v.left}|${v.width}|${v.height}|${v.keyboard}`
      if (viewKey !== prevView) {
        prevView = viewKey
        setView(v)
      }
      const el = findTarget(step.target)
      if (!el) {
        setMissing(true)
      } else {
        setMissing(false)
        // Document coordinates, so the highlight scrolls with the page itself.
        const r = el.getBoundingClientRect()
        const nextBox: Box = {
          top: r.top + window.scrollY - PAD,
          left: r.left + window.scrollX - PAD,
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

  // Measure the rendered card so placement can keep the whole thing on screen.
  useLayoutEffect(() => {
    const h = cardRef.current?.offsetHeight ?? 0
    setCardH((prev) => (Math.abs(prev - h) > 1 ? h : prev))
  })

  if (!active || !step) return null

  const isLast = stepIndex === steps.length - 1
  const cardW = cardWidth(view)
  // Place against the visible area, then shift back into document coordinates.
  const local = box ? { ...box, top: box.top - view.top, left: box.left - view.left } : null
  const fitted = local ? place(local, step.placement ?? 'bottom', cardW, cardH || 180, view.width, view.height) : null
  const placed = fitted
    ? { arrow: fitted.arrow, style: { ...fitted.style, top: fitted.style.top + view.top, left: fitted.style.left + view.left } }
    : null

  // Keyboard up: a slim bar docked to whichever edge is away from the field.
  const docked = view.keyboard
  const dockTop = local ? local.top + local.height / 2 > view.height / 2 : false
  const dockStyle = {
    left: view.left + 8,
    width: view.width - 16,
    top: dockTop ? view.top + 8 : view.top + view.height - 8,
  }

  const requireClick = interaction === 'click'
  const requireInput = interaction === 'input'
  const nextDisabled = requireInput && !satisfied

  return (
    <div className="tour-root" role="dialog" aria-modal="false" aria-label="Guided tour">
      {box && (
        <div
          className="tour-spotlight"
          style={{ top: box.top, left: box.left, width: box.width, height: box.height }}
        />
      )}

      <div
        ref={cardRef}
        className={
          docked ? `tour-card tour-docked${dockTop ? '' : ' tour-docked-bottom'}` : `tour-card tour-arrow-${placed?.arrow ?? 'bottom'}`
        }
        style={
          docked
            ? dockStyle
            : placed
              ? { top: placed.style.top, left: placed.style.left, width: cardW }
              : { top: view.top + 24, left: view.left + 24, width: cardW }
        }
      >
        <div className="tour-step-count">
          Step {stepIndex + 1} of {steps.length}
        </div>
        <h3 className="tour-title">{step.title}</h3>
        {!docked && <p className="tour-body">{step.body}</p>}
        {missing ? (
          <p className="tour-waiting">Follow the highlighted step to continue…</p>
        ) : requireClick ? (
          <p className="tour-waiting">Press the highlighted control to continue.</p>
        ) : requireInput && !satisfied ? (
          <p className="tour-waiting">Enter a value in the highlighted field to continue.</p>
        ) : null}
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
            {!requireClick && (
              <button
                type="button"
                className="tour-btn tour-btn-primary"
                onClick={next}
                disabled={nextDisabled}
              >
                {isLast ? 'Finish' : 'Next'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
