import { useEffect, useRef, useState } from 'react'
import { searchRules, type RuleSearchResult } from '../lib/catalogue'
import { RuleText } from './RuleText'

interface Props {
  systemKey: string
  // When set, unit/ability/weapon hits are clickable and open that datasheet.
  onOpenDatasheet?: (datasheetId: string) => void
  onClose: () => void
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
        <div className="sheet-grip" aria-hidden="true" />
        <div className="rs-head">
          <h3>Rule search</h3>
          <input
            className="rs-input"
            autoFocus
            value={query}
            placeholder="Search abilities, weapons, detachment rules…"
            aria-label="Search rules"
            onChange={(e) => setQuery(e.target.value)}
          />
        </div>

        <div className="rs-results scroll">
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
                  onClick={clickable ? () => onOpenDatasheet!(r.datasheetId!) : undefined}
                >
                  <div className="rs-item-head">
                    <span className="rs-title">{r.title}</span>
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

        <div className="modal-actions">
          <button type="button" className="btn btn-primary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
