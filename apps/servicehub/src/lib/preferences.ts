import { useSyncExternalStore } from 'react'
import { pages } from '../nav/navigation'

/**
 * Per-browser preferences (unit 6.3): the time zone times are shown in, and where ServiceHub opens. Conveniences only —
 * storage can be blocked, and then the defaults (the browser's zone, Simple) apply. Nothing here is shared or sent.
 */
export interface Preferences {
  /** 'browser' or an IANA zone such as 'Europe/London'. */
  readonly timeZone: string
  readonly openOn: 'simple' | 'last'
}

const key = 'servicehub.preferences'
const lastKey = 'servicehub.last-page'
const defaults: Preferences = { timeZone: 'browser', openOn: 'simple' }
const listeners = new Set<() => void>()
let cache: Preferences | null = null

export function readPreferences(): Preferences {
  if (cache) return cache
  try {
    const raw = window.localStorage.getItem(key)
    const parsed = raw ? (JSON.parse(raw) as Partial<Preferences>) : {}
    cache = {
      timeZone: typeof parsed.timeZone === 'string' && isZone(parsed.timeZone) ? parsed.timeZone : defaults.timeZone,
      openOn: parsed.openOn === 'last' ? 'last' : 'simple',
    }
  } catch {
    cache = defaults
  }
  return cache
}

export function writePreferences(patch: Partial<Preferences>): void {
  cache = { ...readPreferences(), ...patch }
  try {
    window.localStorage.setItem(key, JSON.stringify(cache))
  } catch {
    // Not remembered — it still applies for this visit.
  }
  listeners.forEach((l) => l())
}

export function usePreferences(): Preferences {
  return useSyncExternalStore((l) => { listeners.add(l); return () => listeners.delete(l) }, readPreferences, readPreferences)
}

/** The zone to format in, or undefined for the browser's own. */
export function displayTimeZone(): string | undefined {
  const z = readPreferences().timeZone
  return z === 'browser' ? undefined : z
}

export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone
}

function isZone(zone: string): boolean {
  if (zone === 'browser') return true
  try {
    new Intl.DateTimeFormat('en-GB', { timeZone: zone })
    return true
  } catch {
    return false
  }
}

const knownPaths = new Set(pages.map((p) => p.path))

/** Remembers the page last seen, for "Open on: last used". Only known pages; overlays are not remembered. */
export function rememberPage(pathname: string, search: string): void {
  if (!knownPaths.has(pathname)) return
  const kept = new URLSearchParams(search)
  kept.delete('modal')
  kept.delete('panel')
  const q = kept.toString()
  try {
    window.localStorage.setItem(lastKey, q ? `${pathname}?${q}` : pathname)
  } catch {
    // fine
  }
}

export function lastPage(): string | null {
  try {
    const stored = window.localStorage.getItem(lastKey)
    return stored && knownPaths.has(stored.split('?')[0]) ? stored : null
  } catch {
    return null
  }
}
