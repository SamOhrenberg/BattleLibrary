import { useEffect, useRef, useState, type ReactNode } from 'react'
import { searchRules, type RuleSearchResult } from '../lib/catalogue'
import { RuleText } from './RuleText'

interface Props {
  systemKey: string
  // When set, unit/ability/weapon hits are clickable and open that datasheet.
  onOpenDatasheet?: (datasheetId: string, title: string) => void
  onClose: () => void
}

// Server caps results at this many (CatalogueEndpoints.SearchLimit).
const RESULT_CAP = 60

// Wraps case-insensitive occurrences of the term in <mark>.
function highlight(text: string, term: string) {
  if (!term) return text
  const lower = text.toLowerCase()
  const needle = term.toLowerCase()
  const parts: ReactNode[] = []
  let at = 0
  let hit = lower.indexOf(needle)
  while (hit !== -1) {
    if (hit > at) parts.push(text.slice(at, hit))
    parts.push(<mark key={hit}>{text.slice(hit, hit + needle.length)}</mark>)
    at = hit + needle.length
    hit = lower.indexOf(needle, at)
  }
  if (at < text.length) parts.push(text.slice(at))
  return parts
}

const KIND_LABEL: Record<RuleSearchResult['kind'], string> = {
  unit: 'Unit',
  ability: 'Ability',
  weapon: 'Weapon',
  'detachment-rule': 'Detachment rule',
  enhancement: 'Enhancement',
}

// Searches already-cached rules content (unit abilities, weapons, detachment
// rules, enhancements) for the given game system. No new data; read-only.
export function RuleSearch({ systemKey, onOpenDatasheet, onClose }: Props) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<RuleSearchResult[]>([])
  const [loading, setLoading] = useState(false)
  const [searched, setSearched] = useState(false)
  const reqId = useRef(0)

  // Debounced search; a stale response is ignored via the request id.
  useEffect(() => {
    const term = query.trim()
    if (term.length < 2) {
      setResults([])
      setSearched(false)
      setLoading(false)
      return
    }
    setLoading(true)
    const id = ++reqId.current
    const handle = setTimeout(() => {
      searchRules(systemKey, term)
        .then((r) => {
          if (id !== reqId.current) return
          setResults(r)
          setSearched(true)
          setLoading(false)
        })
        .catch(() => {
          if (id !== reqId.current) return
          setResults([])
          setSearched(true)
          setLoading(false)
        })
    }, 300)
    return () => clearTimeout(handle)
  }, [query, systemKey])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal rule-search" onClick={(e) => e.stopPropagation()}>
        <div className="rs-head">
          <div className="sheet-grip" aria-hidden="true" />
          <div className="rs-title-row">
            <h3>Rule search</h3>
            <button type="button" className="rs-close" aria-label="Close rule search" onClick={onClose}>
              ×
            </button>
          </div>
          <div className="rs-input-wrap">
            <input
              className="rs-input"
              type="search"
              enterKeyHint="search"
              autoFocus
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
              value={query}
              placeholder="Abilities, weapons, detachment rules…"
              aria-label="Search rules"
              onChange={(e) => setQuery(e.target.value)}
            />
            {query && (
              <button type="button" className="rs-clear" aria-label="Clear search" onClick={() => setQuery('')}>
                ×
              </button>
            )}
          </div>
          {!loading && searched && results.length > 0 && (
            <p className="rs-count" aria-live="polite">
              {results.length >= RESULT_CAP ? `${RESULT_CAP}+ results, refine to narrow` : `${results.length} result${results.length === 1 ? '' : 's'}`}
            </p>
          )}
        </div>

        <div className="rs-results">
          {query.trim().length < 2 ? (
            <p className="panel-hint">Type at least two letters to search.</p>
          ) : loading ? (
            <p className="hint">Searching…</p>
          ) : results.length === 0 && searched ? (
            <p className="empty">No matches.</p>
          ) : (
            results.map((r, i) => {
              const clickable = !!(onOpenDatasheet && r.datasheetId)
              const Tag = clickable ? 'button' : 'div'
              return (
                <Tag
                  key={`${r.kind}-${r.title}-${i}`}
                  type={clickable ? 'button' : undefined}
                  className={`rs-item${clickable ? ' rs-clickable' : ''}`}
                  onClick={clickable ? () => onOpenDatasheet!(r.datasheetId!, r.kind === 'unit' ? r.title : (r.subtitle ?? r.title)) : undefined}
                >
                  <div className="rs-item-head">
                    <span className="rs-title">{highlight(r.title, query.trim())}</span>
                    <span className="rs-kind">{KIND_LABEL[r.kind]}</span>
                  </div>
                  {r.subtitle && <div className="rs-sub">{r.subtitle}</div>}
                  {r.text && (
                    <div className="rs-text">
                      <RuleText text={r.text} />
                    </div>
                  )}
                </Tag>
              )
            })
          )}
        </div>
      </div>
    </div>
  )
}
