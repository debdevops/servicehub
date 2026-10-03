import { CircleAlert, Eye, RotateCcw, ShieldCheck } from 'lucide-react'
import { useRecoverySummary } from '../../hooks/useRecoverySummary'
import type { CloudProvider } from '../../lib/api/namespaces'
import type { RecoveryWindow } from '../../lib/api/recovery'
import type { ScopeChoice } from '../provider/scopeChoice'
import { RetryLink } from '../ui/RetryLink'

const count = (states: readonly { state: string; count: number }[] | undefined, name: string) => states?.find((s) => s.state === name)?.count ?? 0
const windowWords: Record<RecoveryWindow, string> = { '24h': 'last 24 hours', '7d': 'last 7 days', '30d': 'last 30 days', all: 'all time' }

/**
 * The four numbers above the Replayed table: replayed · stayed fixed · being watched · came back, for the chosen time window. They come
 * from the ONE recovery summary the Advanced ledger also reads (2.12), so the figures can never disagree between Simple and
 * Advanced. "Stayed fixed" is Recovered out of Recovered + Returned, and says how many that was — an Unverified replay is on neither
 * side, and no checkable replay is a dash, never 0% (R4, R5). There is no "change since yesterday": no earlier period is kept.
 */
export function ReplayedNumbers({ provider, choice, window = '24h' }: { provider: CloudProvider; choice: ScopeChoice; window?: RecoveryWindow }) {
  const { data, isPending, isError, refetch } = useRecoverySummary({ window, provider, namespaceId: choice.ns?.id, environment: choice.env ?? undefined, connectedOnly: true })
  // Loading draws the four tiles' shape (6.1), so the table below does not jump when the numbers arrive.
  if (isPending) return <div className="mb-4 grid gap-3 sm:grid-cols-4" aria-hidden="true">{[0, 1, 2, 3].map((i) => <div key={i} className="h-[92px] animate-pulse rounded-2xl bg-[var(--color-border)]" />)}</div>
  if (isError || !data) return <p role="alert" className="mb-4 text-sm text-[var(--color-text-muted)]">ServiceHub couldn’t read the replay numbers just now; the list below is unaffected. <RetryLink onRetry={() => void refetch()} /></p>

  const recovered = count(data.states, 'Recovered')
  const returned = count(data.states, 'Returned')
  const checkable = recovered + returned
  const watching = count(data.states, 'Observing')
  const rate = data.stayedFixedRate
  const words = windowWords[window]
  // The tile counts replays the cloud ACCEPTED; the table below lists every attempt, so it can be longer. Say so rather than show two numbers.
  // Only attempts that were refused or lost count: a Declined entry was never tried, and the list leaves it out.
  const notAccepted = count(data.states, 'ExecutionFailed') + count(data.states, 'ExecutionUnknown')
  const replayedNote = notAccepted > 0 ? `${words} · ${notAccepted.toLocaleString()} more tried, not accepted` : words

  return (
    <section className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-label={`Replays, ${words}`}>
      <Tile Icon={RotateCcw} tone="bg-[#dbeafe] text-[#1d4ed8]" value={data.replaysAccepted.toLocaleString()} label="messages replayed" note={replayedNote} />
      <Tile Icon={ShieldCheck} tone="bg-[#d1fae5] text-[#047857]" box="bg-[#f0fdf4]" value={rate === null ? '—' : `${Math.round(rate * 100)}%`} label="stayed fixed" note={rate === null ? 'nothing has been checked yet' : `${recovered} of ${checkable} that could be checked`} />
      <Tile Icon={Eye} tone="bg-[#f3e8ff] text-[#7e22ce]" box="bg-[#faf5ff]" value={watching.toLocaleString()} label="being watched now" note="inside their watch window" />
      <Tile Icon={CircleAlert} tone="bg-[#fee2e2] text-[#dc2626]" box={returned > 0 ? 'bg-[#fef2f2]' : undefined} value={returned.toLocaleString()} label={returned > 0 ? 'came back — needs a look' : 'came back'} note="failed the same way again" />
    </section>
  )
}

function Tile({ Icon, tone, box, value, label, note }: { Icon: typeof Eye; tone: string; box?: string; value: string; label: string; note: string }) {
  return (
    <div className={`flex items-center gap-4 rounded-2xl border border-[var(--color-border)] px-5 py-4 ${box ?? 'bg-[var(--color-surface)]'}`}>
      <span className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-xl ${tone}`}><Icon className="h-6 w-6" aria-hidden="true" /></span>
      <span className="min-w-0"><span className="tabular block text-2xl font-extrabold leading-none">{value}</span><span className="mt-1 block text-sm font-semibold">{label}</span><span className={`block text-xs ${box ? 'text-[#4b5563]' : 'text-[var(--color-text-muted)]'}`}>{note}</span></span>
    </div>
  )
}
