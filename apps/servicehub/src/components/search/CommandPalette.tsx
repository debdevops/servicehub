import { useQueries } from '@tanstack/react-query'
import { Search } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { helpAnswers } from '../../content/help'
import { namespaceKeys, useNamespaces } from '../../hooks/useNamespaces'
import { fetchEntities, type Entity } from '../../lib/api/namespaces'
import { search, type SearchResult } from '../../lib/searchIndex'
import { visibleEntries } from '../../nav/navigation'
import { useProviderScope } from '../provider/providerScope'

/**
 * ⌘K (unit 6.9): one box to go anywhere. A dialog — focus inside, ↑ ↓ ↵ Esc — built from data the shell already has and the
 * navigation array. The footer says what it searches, and it never searches message contents.
 */
export function CommandPalette({ open, onClose, connectedCloudCount }: { open: boolean; onClose: () => void; connectedCloudCount: number }) {
  const namespaces = useNamespaces()
  const ids = open ? (namespaces.data ?? []).map((n) => n.id) : []
  const entityQueries = useQueries({ queries: ids.map((id) => ({ queryKey: namespaceKeys.entities(id, undefined), queryFn: () => fetchEntities(id), staleTime: 60_000 })) })
  const [q, setQ] = useState('')
  const [active, setActive] = useState(0)
  const input = useRef<HTMLInputElement>(null)
  const navigate = useNavigate()
  const { pathname, search: current } = useLocation()
  const { select } = useProviderScope()

  // A handful of namespaces and a few hundred names — cheap enough to rebuild on every keystroke.
  const entities = new Map<string, readonly Entity[]>()
  entityQueries.forEach((r, i) => r.data && entities.set(ids[i], r.data.entities))
  const results = search(q, namespaces.data ?? [], entities, visibleEntries(connectedCloudCount), helpAnswers)

  useEffect(() => { if (open) { setQ(''); setActive(0); setTimeout(() => input.current?.focus(), 0) } }, [open])
  useEffect(() => setActive(0), [q])
  if (!open) return null

  const go = (r: SearchResult) => {
    if (r.provider) select(r.provider)
    if (r.href.startsWith('?')) {
      const next = new URLSearchParams(current)
      new URLSearchParams(r.href.slice(1)).forEach((v, k) => next.set(k, v))
      navigate(`${pathname}?${next}`)
    } else navigate(r.href)
    onClose()
  }
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') { e.preventDefault(); onClose() }
    else if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => Math.min(results.length - 1, a + 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => Math.max(0, a - 1)) }
    else if (e.key === 'Enter' && results[active]) { e.preventDefault(); go(results[active]) }
    else if (e.key === 'Tab') e.preventDefault() // focus stays in the dialog
  }

  let lastGroup = ''
  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-black/30 p-6 pt-[12vh]" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div role="dialog" aria-modal="true" aria-label="Search" onKeyDown={onKey} className="w-full max-w-xl overflow-hidden rounded-2xl bg-[var(--color-surface)] shadow-2xl">
        <label className="flex items-center gap-2 border-b border-[var(--color-border)] px-4 py-3">
          <Search className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
          <span className="sr-only">Search</span>
          <input ref={input} value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search clouds, queues and places…" role="combobox" aria-expanded={results.length > 0} aria-controls="palette-results" aria-activedescendant={results[active] ? `pr-${active}` : undefined} className="w-full bg-transparent text-[15px] outline-none" />
          <kbd className="rounded border border-[var(--color-border)] px-1.5 text-xs text-[var(--color-text-muted)]">Esc</kbd>
        </label>
        <ul id="palette-results" role="listbox" aria-label="Results" className="max-h-[50vh] overflow-y-auto py-1">
          {q.trim() && results.length === 0 && <li className="px-4 py-6 text-center text-sm text-[var(--color-text-muted)]">Nothing matches “{q}”.</li>}
          {results.map((r, i) => {
            const heading = r.group !== lastGroup ? r.group : null
            lastGroup = r.group
            return (
              <li key={r.key} role="none">
                {heading && <p className="px-4 pb-1 pt-2 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">{heading}</p>}
                <button type="button" id={`pr-${i}`} role="option" aria-selected={i === active} onMouseEnter={() => setActive(i)} onClick={() => go(r)}
                  className={`block w-full px-4 py-2 text-left ${i === active ? 'bg-[var(--color-primary-50)]' : ''}`}>
                  <span className="block text-sm font-semibold">{r.title}</span>
                  <span className="block truncate text-xs text-[var(--color-text-muted)]">{r.detail}</span>
                </button>
              </li>
            )
          })}
        </ul>
        <p className="border-t border-[var(--color-border)] bg-[var(--color-surface-muted)] px-4 py-2 text-[11px] text-[var(--color-text-muted)]">
          Searches queue, topic and namespace names, places and help — never message contents.
        </p>
      </div>
    </div>
  )
}
