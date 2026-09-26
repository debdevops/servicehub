import { useEffect } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'

const typing = (t: EventTarget | null) =>
  t instanceof HTMLElement && (t.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(t.tagName))

/**
 * The shortcuts Help lists (units 6.4, 6.6), and only those: ⌘K/Ctrl-K search · ? help · A switch Simple/Advanced ·
 * R replay the open message (opens the proposal, never executes) · / filter the table. A single key never fires in a text
 * field or with a modifier; Esc belongs to whatever is open.
 */
export function useShortcuts({ openSearch, simpleHref, advancedHref }: { openSearch: () => void; simpleHref: string; advancedHref: string }) {
  const navigate = useNavigate()
  const { pathname, search } = useLocation()

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); openSearch(); return }
      if (e.metaKey || e.ctrlKey || e.altKey || typing(e.target)) return
      const params = new URLSearchParams(search)
      const open = (k: string, v: string) => { params.set(k, v); navigate(`${pathname}?${params}`) }
      if (e.key === '?') { e.preventDefault(); open('panel', 'help') }
      else if (e.key === 'a' || e.key === 'A') { e.preventDefault(); navigate(pathname.startsWith('/advanced') ? simpleHref : advancedHref) }
      else if ((e.key === 'r' || e.key === 'R') && params.get('message') && !params.get('modal')) { e.preventDefault(); open('modal', 'replay') }
      else if (e.key === '/') {
        const filter = document.querySelector<HTMLElement>('[data-shortcut="filter"]')
        if (filter) { e.preventDefault(); filter.focus() }
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [navigate, pathname, search, openSearch, simpleHref, advancedHref])
}
