import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import type { CloudProvider } from '../../lib/api/namespaces'
import { ProviderScopeContext, readStoredProvider, storeProvider } from './providerScope'

/**
 * Holds which connected cloud the product is scoped to (IA §2.3).
 *
 * The choice is sticky across refreshes, but only ever resolves to a cloud that is connected right
 * now: a remembered AWS is ignored once AWS is removed. The provider accent is applied to the root
 * element, so `:root[data-provider]` re-tints accents — the selected row, the active icon — and
 * never the chrome.
 */
export function ProviderScopeProvider({
  connected,
  children,
}: {
  connected: readonly CloudProvider[]
  children: ReactNode
}) {
  const [chosen, setChosen] = useState<CloudProvider | null>(readStoredProvider)

  const selected = chosen !== null && connected.includes(chosen) ? chosen : (connected[0] ?? null)

  const select = useCallback((provider: CloudProvider) => {
    setChosen(provider)
    storeProvider(provider)
  }, [])

  useEffect(() => {
    const root = document.documentElement
    if (selected === null || selected === 'azure') delete root.dataset.provider
    else root.dataset.provider = selected
    return () => {
      delete root.dataset.provider
    }
  }, [selected])

  const value = useMemo(() => ({ selected, select }), [selected, select])
  return <ProviderScopeContext.Provider value={value}>{children}</ProviderScopeContext.Provider>
}
