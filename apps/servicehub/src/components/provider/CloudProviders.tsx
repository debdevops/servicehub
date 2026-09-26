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
const glyphColor: Record<string, string> = { azure: '#0284c7', aws: '#f97316', gcp: '#22c55e' }

export function CloudProviders({ providers }: { providers: readonly ConnectedProvider[] }) {
  const { selected, select } = useProviderScope()
  const { pathname, search } = useLocation()
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
                // A namespace or environment scope belongs to the cloud it was chosen in, so switching clouds drops it.
                if (pathname !== '/' || /[?&](ns|env)=/.test(search)) navigate('/')
              }}
              className={[
                'mb-[5px] flex w-full items-center gap-[11px] rounded-[10px] border px-3 py-[9px] text-left transition-colors',
                isSelected
                  ? 'border-[var(--color-primary-200)] bg-[var(--color-primary-50)]'
                  : 'border-transparent hover:bg-[var(--color-surface-muted)]',
              ].join(' ')}
            >
              <span
                aria-hidden="true"
                className="flex h-[26px] w-[26px] shrink-0 items-center justify-center rounded-[7px] text-[11px] font-extrabold"
                style={{ background: `${glyphColor[p.provider]}22`, color: glyphColor[p.provider] }}
              >
                {p.label.charAt(0)}
              </span>
              <span className="min-w-0">
                <span className="block text-[13px] font-semibold leading-tight text-[var(--color-text)]">{p.label}</span>
                <span
                  className={`flex items-center gap-1 text-[10.5px] leading-tight ${p.needsAttention ? 'text-[#92400e]' : 'text-[#047857]'}`}
                >
                  <span className={`inline-block h-1.5 w-1.5 rounded-full ${p.needsAttention ? 'bg-[var(--color-warning)]' : 'bg-[var(--color-success)]'}`} />
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
