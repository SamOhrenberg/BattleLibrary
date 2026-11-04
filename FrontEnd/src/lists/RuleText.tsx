import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import type { AbilityText } from '../lib/catalogue'

// Renders BSData rules text, which marks up spans with **double asterisks** for
// bold and ^^double carets^^ for keywords (the two nest in either order, e.g.
// **^^Terminator Captain^^**). Markers toggle styling; nodes are built rather
// than injecting HTML, so the text stays safe.
//
// When `rules` is given, any rule name that appears in the text (plain or
// bracketed, e.g. Deep Strike or [DEVASTATING WOUNDS]) becomes a RuleRef: a
// hover/tap target that opens a tooltip with that rule's text.
export function RuleText({ text, rules }: { text: string; rules?: AbilityText[] }) {
  const lookup = rules && rules.length > 0 ? buildLookup(rules) : null
  const matcher = lookup ? buildMatcher([...lookup.keys()]) : null

  const nodes: ReactNode[] = []
  let buffer = ''
  let bold = false
  let keyword = false
  let key = 0

  const flush = () => {
    if (!buffer) return
    const children: ReactNode = matcher && lookup ? injectRefs(buffer, matcher, lookup) : buffer
    buffer = ''
    if (bold && keyword) nodes.push(<strong className="kw-text" key={key++}>{children}</strong>)
    else if (bold) nodes.push(<strong key={key++}>{children}</strong>)
    else if (keyword) nodes.push(<span className="kw-text" key={key++}>{children}</span>)
    else nodes.push(<span key={key++}>{children}</span>)
  }

  for (let i = 0; i < text.length; i++) {
    const pair = text[i] + text[i + 1]
    if (pair === '**') {
      flush()
      bold = !bold
      i++
    } else if (pair === '^^') {
      flush()
      keyword = !keyword
      i++
    } else {
      buffer += text[i]
    }
  }
  flush()

  return <>{nodes}</>
}

// Normalizes a rule name to its match key: drop brackets, lowercase, collapse
// whitespace. So "Deep Strike", "[DEEP STRIKE]" and "deep  strike" all align.
const normalizeRule = (s: string) => s.replace(/[[\]]/g, '').trim().toLowerCase().replace(/\s+/g, ' ')

// Map of normalized rule name -> its proper name and text.
function buildLookup(rules: AbilityText[]): Map<string, { name: string; text: string }> {
  const map = new Map<string, { name: string; text: string }>()
  for (const r of rules) {
    const key = normalizeRule(r.name)
    if (key && !map.has(key)) map.set(key, { name: r.name, text: r.text })
  }
  return map
}

const escapeRegex = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

// One case-insensitive regex matching any rule name, optionally wrapped in
// brackets. Longest names first so "Deadly Demise" wins over a shorter overlap.
function buildMatcher(keys: string[]): RegExp | null {
  const parts = keys
    .filter((k) => k.length > 0)
    .sort((a, b) => b.length - a.length)
    .map((k) => `\\[?\\b${escapeRegex(k)}\\b\\]?`)
  return parts.length > 0 ? new RegExp(parts.join('|'), 'gi') : null
}

// Splits a text run on rule-name matches, wrapping each match in a RuleRef.
function injectRefs(content: string, matcher: RegExp, lookup: Map<string, { name: string; text: string }>): ReactNode {
  const out: ReactNode[] = []
  let last = 0
  let k = 0
  matcher.lastIndex = 0
  let m: RegExpExecArray | null
  while ((m = matcher.exec(content)) !== null) {
    const rule = lookup.get(normalizeRule(m[0]))
    if (rule === undefined) continue
    if (m.index > last) out.push(content.slice(last, m.index))
    out.push(<RuleRef key={k++} label={m[0]} name={rule.name} text={rule.text} />)
    last = m.index + m[0].length
  }
  if (out.length === 0) return content
  if (last < content.length) out.push(content.slice(last))
  return out
}

// An inline reference to a rule: shows the rule text in a tooltip on hover
// (desktop) or tap (mobile), and toggles on click/Enter/Space. Closes on
// outside tap or Escape. The popover is positioned with fixed coordinates
// measured from the trigger, so it escapes the modal's scroll container rather
// than being clipped by it.
function RuleRef({ label, name, text }: { label: string; name: string; text: string }) {
  const [open, setOpen] = useState(false)
  const [coords, setCoords] = useState<{ top: number; left: number } | null>(null)
  const triggerRef = useRef<HTMLSpanElement>(null)
  const popRef = useRef<HTMLSpanElement>(null)

  useLayoutEffect(() => {
    if (!open) {
      setCoords(null)
      return
    }
    const place = () => {
      const trigger = triggerRef.current
      const pop = popRef.current
      if (!trigger || !pop) return
      const t = trigger.getBoundingClientRect()
      const margin = 8
      let left = t.left
      if (left + pop.offsetWidth > window.innerWidth - margin) {
        left = window.innerWidth - margin - pop.offsetWidth
      }
      if (left < margin) left = margin
      let top = t.bottom + 6
      if (top + pop.offsetHeight > window.innerHeight - margin && t.top - 6 - pop.offsetHeight >= margin) {
        top = t.top - 6 - pop.offsetHeight
      }
      setCoords({ top, left })
    }
    place()
    window.addEventListener('scroll', place, true)
    window.addEventListener('resize', place)
    return () => {
      window.removeEventListener('scroll', place, true)
      window.removeEventListener('resize', place)
    }
  }, [open])

  useEffect(() => {
    if (!open) return
    const onDown = (e: PointerEvent) => {
      if (triggerRef.current && !triggerRef.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('pointerdown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <span
      ref={triggerRef}
      className="rule-ref"
      role="button"
      tabIndex={0}
      aria-expanded={open}
      onClick={(e) => {
        e.stopPropagation()
        setOpen((o) => !o)
      }}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
      onFocus={() => setOpen(true)}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          setOpen((o) => !o)
        }
      }}
    >
      {label}
      {open && (
        <span
          ref={popRef}
          className="rule-ref-pop"
          role="tooltip"
          style={{ top: coords?.top ?? 0, left: coords?.left ?? 0, visibility: coords ? 'visible' : 'hidden' }}
        >
          <span className="rule-ref-title">{name}</span>
          <RuleText text={text} />
        </span>
      )}
    </span>
  )
}
