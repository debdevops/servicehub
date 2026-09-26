import type { CloudProvider } from '../api/namespaces'

const KEY = 'servicehub.demo'

/**
 * Demo mode (unit 6.5): a mode of this app, not a second app. While it is on, the one API client answers from believable
 * made-up data instead of a server, and nothing is ever sent anywhere. It lasts for the browser session.
 */
export function isDemo(): boolean {
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
  } catch {
    /* nothing to undo */
  }
}

export const demoProviders: readonly CloudProvider[] = ['azure', 'aws', 'gcp']
