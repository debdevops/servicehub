import { Cloud } from 'lucide-react'
import { useLocation, useNavigate } from 'react-router-dom'
import type { ConnectedProvider } from '../../lib/providers'
import { useProviderScope } from './providerScope'

/**
 * The Cloud Providers rows: exactly the clouds that are connected, nothing else (IA §3).
 *
 * Choosing one scopes Home to it. From Fleet Overview — which is cross-cloud and not scoped — that
 * also takes you to Home, because choosing a cloud from there means "show me that one".
 * The provider accent touches the selected row only; the rest of the chrome does not change.
 */
export function CloudProviders({ providers }: { providers: readonly ConnectedProvider[] }) {
  const { selected, select } = useProviderScope()
  const { pathname } = useLocation()
  const navigate = useNavigate()

  return (
    <ul aria-label="Connected clouds">
      {providers.map((p) => {
        const isSelected = p.provider === selected
        return (
          <li key={p.provider}>
            <button
              type="button"
              aria-pressed={isSelected}
              onClick={() => {
                select(p.provider)
                if (pathname !== '/') navigate('/')
              }}
              className={[
                'mb-0.5 flex w-full items-center gap-2.5 rounded-lg border-l-[3px] px-3 py-2 text-left text-sm transition-colors',
                isSelected
                  ? 'border-[var(--color-accent)] bg-[var(--color-surface-muted)] font-medium'
                  : 'border-transparent hover:bg-[var(--color-surface-muted)]',
              ].join(' ')}
            >
              <Cloud className={`h-4 w-4 shrink-0 ${isSelected ? 'text-[var(--color-accent)]' : 'text-[var(--color-text-muted)]'}`} />
              <span className="min-w-0">
                <span className="block text-[var(--color-text)]">{p.label}</span>
                <span
                  className={`block text-xs font-normal ${p.needsAttention ? 'text-[var(--color-warning)]' : 'text-[var(--color-text-muted)]'}`}
                >
                  {p.needsAttention ? 'Could not connect at last check' : 'Connected'} · {p.namespaceCount}{' '}
                  {p.namespaceCount === 1 ? 'namespace' : 'namespaces'}
                </span>
              </span>
            </button>
          </li>
        )
      })}
    </ul>
  )
}
