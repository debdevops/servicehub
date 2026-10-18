import { Search } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import { useComponents } from '../../hooks/useComponents'
import { nextHelpAnswers } from '../content/help'
import { cloudOf, parseScope } from '../lib/clouds'
import { searchNext } from '../lib/searchIndex'
import type { NextOverlayBodyProps } from './registry'

/**
 * Search (build-plan unit F7; design `d10-search`), at `?dialog=search` and on ⌘K / Ctrl-K: connections, queues and subscriptions, pages,
 * the dialogs that are built, and Help — across every cloud, whatever page you are on. It never searches inside message bodies, and says so.
 * ↑ ↓ move, Enter opens, Esc closes.
 */
// eslint-disable-next-line @typescript-eslint/no-unused-vars
export default function SearchDialog(_props: NextOverlayBodyProps) {
  const [q, setQ] = useState('')
  const [active, setActive] = useState(0)
  const input = useRef<HTMLInputElement>(null)
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const components = useComponents(null)
  const cloud = cloudOf(parseScope(pathname.split('/')[2])) ?? 'all'
  useOverlayTitle('Search', '⌘K · connections, queues, pages and actions on every cloud')
  const results = searchNext(q, components.data?.components ?? [], nextHelpAnswers, cloud, pathname)
  useEffect(() => setActive(0), [q])
  useEffect(() => { input.current?.focus() }, [])

  const go = (href: string) => { navigate(href); }
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => Math.min(results.length - 1, a + 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => Math.max(0, a - 1)) }
    else if (e.key === 'Enter' && results[active]) { e.preventDefault(); go(results[active].href) }
  }
  let last = ''
  return (
    <div className="space-y-3" onKeyDown={onKey}>
      <label className="flex items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2.5">
        <Search className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
        <span className="sr-only">Search</span>
        <input ref={input} value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search connections, queues, pages…" role="combobox" aria-expanded={results.length > 0}
          aria-controls="next-search-results" aria-activedescendant={results[active] ? `ns-${active}` : undefined} className="w-full bg-transparent text-[15px] outline-none" />
      </label>
      <ul id="next-search-results" role="listbox" aria-label="Results" className="max-h-[50vh] overflow-y-auto">
        {q.trim() && results.length === 0 && <li className="px-2 py-4 text-sm text-[var(--color-text-muted)]">Nothing matches “{q}”.</li>}
        {results.map((r, i) => {
          const heading = r.group !== last ? r.group : null
          last = r.group
          return (
            <li key={r.key} role="none">
              {heading && <p className="px-2 pb-1 pt-3 text-sm font-bold">{heading}</p>}
              <button type="button" id={`ns-${i}`} role="option" aria-selected={i === active} onMouseEnter={() => setActive(i)} onClick={() => go(r.href)}
                className={`flex w-full items-baseline gap-3 rounded-md px-2 py-2 text-left text-sm ${i === active ? 'bg-[var(--color-primary-50)]' : ''}`}>
                <span className="font-semibold">{r.title}</span><span className="truncate text-xs text-[var(--color-text-muted)]">{r.detail}</span>
              </button>
            </li>
          )
        })}
      </ul>
      <p role="note" className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-3.5 py-2.5 text-sm"><b>Search finds places and actions.</b> It never searches inside message bodies: ServiceHub keeps only a short preview of each message, and a body can hold personal data.</p>
      <p className="text-xs text-[var(--color-text-muted)]">↑ ↓ to move · Enter to open · Esc to close</p>
    </div>
  )
}
