import { pages } from '../nav/navigation'

const storageKey = 'servicehub.advanced.last'
const advancedPaths = new Set(pages.filter((p) => p.surface === 'advanced').map((p) => p.path))

/**
 * Where the Advanced segment of the switch goes: the last Advanced page seen in this browser.
 * A convenience only — storage can be blocked or hold something stale, and either way the answer is
 * simply the Advanced landing page. A first visit therefore always starts at the top.
 */
export function readLastAdvancedPath(fallback: string): string {
  try {
    const stored = window.localStorage.getItem(storageKey)
    return stored !== null && advancedPaths.has(stored) ? stored : fallback
  } catch {
    return fallback
  }
}

export function rememberAdvancedPath(pathname: string): void {
  if (!advancedPaths.has(pathname)) return
  try {
    window.localStorage.setItem(storageKey, pathname)
  } catch {
    // Not remembering is fine; the switch still works.
  }
}
