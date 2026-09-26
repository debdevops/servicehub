import { displayTimeZone } from './preferences'

/** Small, dependable formatters. Every one takes its clock as an argument so a test never depends on "now". */

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const kb = bytes / 1024
  if (kb < 1024) return `${kb < 10 ? kb.toFixed(1) : Math.round(kb)} KB`
  const mb = kb / 1024
  return `${mb < 10 ? mb.toFixed(1) : Math.round(mb)} MB`
}

/** "4 h", "12 min", "3 d" — how long something has been waiting. Never negative. */
export function formatAge(fromIso: string, now: Date): string {
  const seconds = Math.max(0, Math.floor((now.getTime() - new Date(fromIso).getTime()) / 1000))
  if (seconds < 60) return 'just now'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes} min`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} h`
  return `${Math.floor(hours / 24)} d`
}

/**
 * "10:12" for today, otherwise "Sep 22, 10:12" — 24-hour, in the time zone chosen in Settings (the browser's own by default).
 * `timeZone` is for tests; the app passes nothing and the preference applies.
 */
export function formatWhen(iso: string, now: Date, timeZone: string | undefined = displayTimeZone()): string {
  const d = new Date(iso)
  const clock = d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', timeZone })
  const day = (x: Date) => x.toLocaleDateString('en-CA', { timeZone })
  return day(d) === day(now) ? clock : `${d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone })}, ${clock}`
}
