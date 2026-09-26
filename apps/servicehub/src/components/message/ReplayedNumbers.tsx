import { useRecoverySummary } from '../../hooks/useRecoverySummary'
import type { CloudProvider } from '../../lib/api/namespaces'
import type { ScopeChoice } from '../provider/scopeChoice'

const count = (states: readonly { state: string; count: number }[] | undefined, name: string) => states?.find((s) => s.state === name)?.count ?? 0

/**
 * The four numbers above the Replayed table: replayed today · stayed fixed · being watched · came back. They come
 * from the ONE recovery summary the Advanced ledger also reads (2.12), so the figures can never disagree between
 * Simple and Advanced. Nothing is computed here beyond reading them; "stayed fixed" is Recovered out of Recovered +
 * Returned, and says how many that was — an Unverified replay is on neither side, and no checkable replay is a dash,
 * never 0% (R4, R5).
 */
export function ReplayedNumbers({ provider, choice }: { provider: CloudProvider; choice: ScopeChoice }) {
  const { data, isPending, isError } = useRecoverySummary({ window: '24h', provider, namespaceId: choice.ns?.id, environment: choice.env ?? undefined })
  // Loading draws the four tiles' shape (6.1), so the table below does not jump when the numbers arrive.
  if (isPending) return <div className="mb-4 grid gap-3 sm:grid-cols-4" aria-hidden="true">{[0, 1, 2, 3].map((i) => <div key={i} className="h-[74px] animate-pulse rounded-xl bg-[var(--color-surface-muted)]" />)}</div>
  if (isError || !data) return <p role="alert" className="mb-4 text-sm text-[var(--color-text-muted)]">ServiceHub couldn’t read the replay numbers just now; the list below is unaffected.</p>

  const recovered = count(data.states, 'Recovered')
  const returned = count(data.states, 'Returned')
  const checkable = recovered + returned
  const watching = count(data.states, 'Observing')
  const rate = data.stayedFixedRate

  return (
    <div className="mb-4 grid gap-3 sm:grid-cols-4" aria-label="Replays in the last 24 hours">
      <Tile value={data.replaysAccepted.toLocaleString()} note="messages replayed, last 24 hours" />
      <Tile
        tone="good"
        value={rate === null ? '—' : `${Math.round(rate * 100)}%`}
        note={rate === null ? 'nothing has been checked yet' : `stayed fixed — ${recovered} of ${checkable} that could be checked`}
      />
      <Tile value={watching.toLocaleString()} note="being watched now" />
      <Tile tone={returned > 0 ? 'bad' : undefined} value={returned.toLocaleString()} note={returned > 0 ? 'came back — needs a look' : 'came back'} />
    </div>
  )
}

function Tile({ value, note, tone }: { value: string; note: string; tone?: 'good' | 'bad' }) {
  const box = tone === 'good' ? 'bg-[var(--color-success-light)]' : tone === 'bad' ? 'bg-[var(--color-error-light)]' : 'bg-[var(--color-surface)]'
  return (
    <div className={`rounded-xl border border-[var(--color-border)] p-4 ${box}`}>
      <div className="tabular text-2xl font-semibold">{value}</div>
      <div className="mt-0.5 text-xs text-[var(--color-text-muted)]">{note}</div>
    </div>
  )
}
