import { useEffect } from 'react'
import { useSearchParams } from 'react-router-dom'
import { asCloud } from '../../components/provider/scopeChoice'
import { useProviderScope } from '../../components/provider/providerScope'
import type { CloudProvider } from '../api/namespaces'

export type HomeWindow = '24h' | '7d'

export interface HomeScope {
  /** `null` means All clouds. With exactly one cloud connected this is always that cloud — there is
   *  nothing else it could mean, and no control is drawn for it. */
  readonly provider: CloudProvider | null
  readonly window: HomeWindow
  /** Selects a cloud (or `null` for All clouds) — the same state a scope tab, a cloud card and the
   *  sidebar's cloud row all set (D48, plan §7 "scope is one state"). Drops `?ns=` / `?env=`: they
   *  belonged to whatever was in scope before. */
  readonly setProvider: (provider: CloudProvider | null) => void
  readonly setWindow: (window: HomeWindow) => void
}

const asWindow = (v: string | null): HomeWindow => (v === '7d' ? '7d' : '24h')

/**
 * Home's one piece of URL state (D48): which cloud is in view (`?provider=`, absent = All clouds) and
 * the page's window (`?window=24h|7d`, O-H2). An invalid or unconnected `?provider=` is ignored, not
 * trusted — a stale link or a typo shows All clouds rather than an error.
 */
export function useHomeScope(connected: readonly CloudProvider[]): HomeScope {
  const [params, setParams] = useSearchParams()
  const requested = asCloud(params.get('provider'))
  const provider = requested && connected.includes(requested) ? requested : connected.length === 1 ? connected[0] : null

  // "Scope is one state": the sidebar's Dead letters / Active / Replayed links resolve to the sticky choice, so whatever cloud this URL
  // shows must also be that choice — otherwise picking AWS on a scope tab (or opening a `?provider=aws` link) and then clicking
  // "Dead letters" lands on whichever cloud was chosen last, Azure by default.
  const { selected, select } = useProviderScope()
  useEffect(() => {
    if (requested && provider === requested && selected !== requested) select(requested)
  }, [requested, provider, selected, select])

  return {
    provider,
    window: asWindow(params.get('window')),
    setProvider: (next) =>
      setParams((current) => {
        const n = new URLSearchParams(current)
        if (next) n.set('provider', next)
        else n.delete('provider')
        n.delete('ns')
        n.delete('env')
        return n
      }),
    setWindow: (next) =>
      setParams((current) => {
        const n = new URLSearchParams(current)
        n.set('window', next)
        return n
      }),
  }
}
