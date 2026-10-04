import type { CloudProvider } from '../api/namespaces'

const KEY = 'servicehub.demo'

/**
 * A build that is ONLY the demo (`VITE_DEMO_ONLY=true`): the public demo page. There is no server behind it, so demo mode is
 * always on and cannot be left.
 */
export const DEMO_ONLY: boolean = import.meta.env.VITE_DEMO_ONLY === 'true'

/**
 * Demo mode (unit 6.5): a mode of this app, not a second app. While it is on, the one API client answers from believable
 * made-up data instead of a server, and nothing is ever sent anywhere. It lasts for the browser session.
 */
export function isDemo(): boolean {
  if (DEMO_ONLY) return true
  try {
    return window.sessionStorage.getItem(KEY) === 'on'
  } catch {
    return false
  }
}

export function enterDemo(): void {
  try {
    window.sessionStorage.setItem(KEY, 'on')
  } catch {
    /* a blocked sessionStorage just means no demo — the real app still works */
  }
}

export function leaveDemo(): void {
  try {
    window.sessionStorage.removeItem(KEY)
    window.sessionStorage.removeItem('servicehub.demo.world')
  } catch {
    /* nothing to undo */
  }
}

/** Throws away everything done in the demo and starts it from the beginning. */
export async function resetDemo(): Promise<void> {
  const { resetWorld } = await import('./world/store')
  resetWorld()
}

export const demoProviders: readonly CloudProvider[] = ['azure', 'aws', 'gcp']
