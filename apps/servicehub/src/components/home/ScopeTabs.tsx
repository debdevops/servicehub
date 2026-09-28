import { useQueries } from '@tanstack/react-query'
import { cloudColor } from '../provider/scopeChoice'
import { useNamespaces, namespaceKeys } from '../../hooks/useNamespaces'
import { fetchNamespaceStats, type CloudProvider } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'

/**
 * Home's one scope control (D48): All clouds, then one tab per connected cloud carrying that cloud's
 * own dead-letter total (never a sum across them, R5). Only drawn with 2+ clouds connected — with one,
 * there is nothing to switch between, so no control is shown at all.
 */
export function ScopeTabs({ connected, selected, onSelect }: {
  connected: readonly CloudProvider[]
  selected: CloudProvider | null
  onSelect: (provider: CloudProvider | null) => void
}) {
  const namespaces = useNamespaces()
  const list = namespaces.data ?? []
  const stats = useQueries({ queries: list.map((n) => ({ queryKey: namespaceKeys.stats(n.id), queryFn: () => fetchNamespaceStats(n.id) })) })
  const totalOf = (p: CloudProvider): number | null => {
    const mine = list.filter((n) => n.provider === p)
    const counts = mine.map((n) => stats[list.findIndex((x) => x.id === n.id)]?.data?.deadLetterMessages)
    return counts.length > 0 && counts.every((v) => typeof v === 'number') ? (counts as number[]).reduce((a, b) => a + b, 0) : null
  }

  return (
    <div role="tablist" aria-label="Scope" className="flex flex-wrap items-center gap-2 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-1.5 shadow-[var(--shadow-card)]">
      <Tab on={selected === null} onClick={() => onSelect(null)}>
        All clouds <Count value={list.length} suffix={list.length === 1 ? 'namespace' : 'namespaces'} on={selected === null} />
      </Tab>
      {connected.map((p) => (
        <Tab key={p} on={selected === p} onClick={() => onSelect(p)}>
          <span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: cloudColor[p] }} /> {providerLabel[p]}{' '}
          <CountOrCant value={totalOf(p)} on={selected === p} />
        </Tab>
      ))}
      <span className="ml-auto hidden px-2 text-[11.5px] text-[var(--color-text-muted)] sm:inline">Each cloud keeps its own numbers — nothing is added across clouds.</span>
    </div>
  )
}

function Tab({ on, onClick, children }: { on: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={on}
      onClick={onClick}
      className={`flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-[13px] font-semibold transition-colors ${
        on ? 'bg-[var(--color-primary-600)] text-white shadow-[0_2px_6px_rgba(2,132,199,.28)]' : 'text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]'
      }`}
    >
      {children}
    </button>
  )
}

function Count({ value, suffix, on }: { value: number; suffix: string; on: boolean }) {
  return <span className={`rounded-full px-1.5 py-0.5 text-[11px] font-bold ${on ? 'bg-[#075985]' : 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]'}`}>{value} {suffix}</span>
}

function CountOrCant({ value, on }: { value: number | null; on: boolean }) {
  return (
    <span className={`rounded-full px-1.5 py-0.5 text-[11px] font-bold ${on ? 'bg-[#075985]' : value === null ? 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]' : 'bg-[var(--color-error-light)] text-[#b91c1c]'}`}>
      {value === null ? 'can’t count' : value.toLocaleString()}
    </span>
  )
}
