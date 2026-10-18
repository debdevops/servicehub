import { useEffect } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { nextEntry, nextHref } from '../nav/navigation'

const typing = (t: EventTarget | null) =>
  t instanceof HTMLElement && (t.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(t.tagName))

/**
 * The shell's keyboard shortcuts (`content/shortcuts.ts` lists exactly these): ⌘K / Ctrl-K Search from anywhere, `?` Help, `/` focus the
 * list's search box (an element marked `data-shortcut="filter"`), and `R` replay the open message. **R presses the Replay link that the
 * open Message dialog is already offering** (`data-shortcut="replay"`), so it can only ever do what that link does — only for a message
 * still in dead-letter, that nothing is holding, for someone allowed to replay it — and it opens the preview, never a replay. A single
 * key never fires inside a text field or with a modifier.
 */
export function useNextShortcuts() {
  const navigate = useNavigate()
  const { pathname, search } = useLocation()

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); navigate(nextHref(nextEntry('search'), {}, pathname)); return }
      if (e.metaKey || e.ctrlKey || e.altKey || typing(e.target)) return
      const params = new URLSearchParams(search)
      if (e.key === '?') {
        if (params.get('panel') === 'help') return
        e.preventDefault()
        params.set('panel', 'help')
        navigate(`${pathname}?${params}`)
      } else if (e.key === '/') {
        const box = document.querySelector<HTMLElement>('[data-shortcut="filter"]')
        if (box) { e.preventDefault(); box.focus() }
      } else if (e.key === 'r' || e.key === 'R') {
        const replay = document.querySelector<HTMLElement>('[data-shortcut="replay"]')
        if (replay) { e.preventDefault(); replay.click() }
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [navigate, pathname, search])
}
