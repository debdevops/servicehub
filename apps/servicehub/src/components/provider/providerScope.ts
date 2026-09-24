import { createContext, useContext } from 'react'
import type { CloudProvider } from '../../lib/api/namespaces'

export interface ProviderScope {
  /** The cloud the page is working in. Null until at least one cloud is connected. */
  readonly selected: CloudProvider | null
  readonly select: (provider: CloudProvider) => void
}

export const ProviderScopeContext = createContext<ProviderScope>({ selected: null, select: () => {} })

/** The one place a page learns which cloud it is scoped to. Nothing else keeps its own copy. */
export function useProviderScope(): ProviderScope {
  return useContext(ProviderScopeContext)
}

const storageKey = 'servicehub.provider'

/** Storage can be blocked or throw (private windows, cleared site data) — the product must work without it. */
export function readStoredProvider(): CloudProvider | null {
  try {
    const value = window.localStorage.getItem(storageKey)
    return value === 'azure' || value === 'aws' || value === 'gcp' ? value : null
  } catch {
    return null
  }
}

export function storeProvider(provider: CloudProvider): void {
  try {
    window.localStorage.setItem(storageKey, provider)
  } catch {
    // Not remembering the choice is a small loss; failing to switch cloud would be a large one.
  }
}
