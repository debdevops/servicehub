import { Check, CircleHelp, Minus, RotateCcw, TriangleAlert } from 'lucide-react'
import { useRecoverySummary } from '../../hooks/useRecoverySummary'
import type { EntryState } from '../../lib/api/recovery'
import type { CloudProvider, EnvironmentKind } from '../../lib/api/namespaces'
import { stateChip, stateMeaning, stateTone } from '../../lib/ledgerWords'
import { providerLabel } from '../../lib/providers'
import { InsightCard } from './InsightCard'

// Status colours are reserved for state, and each ships with an icon and a word — colour is never the only carrier.
const TONE = {
  good: { fill: '#059669', Icon: Check },
  bad: { fill: '#dc2626', Icon: RotateCcw },
  warn: { fill: '#d97706', Icon: CircleHelp },
  neutral: { fill: '#6b7280', Icon: Minus },
} as const

// Bad news first, so a returning failure is never the last thing in the row.
const ORDER: readonly EntryState[] = ['Returned', 'ExecutionFailed', 'ExecutionUnknown', 'Unverified', 'Observing', 'Executing', 'Recovered', 'Discarded', 'WrittenOff', 'Expired', 'Declined']

/**
 * How the last week's recoveries ended, on any cloud, from the same summary the Ledger uses — so the numbers here are the
 * Ledger's. "Stayed fixed" counts only what was PROVEN either way: an Unverified replay is on neither side of the rate (V1),
 * and where nothing was proven the card says so instead of showing a percentage.
 */
export function RecoveryOutcomes({ provider, namespaceId, environment }: { provider: CloudProvider; namespaceId?: string; environment?: EnvironmentKind }) {
  const cloud = providerLabel[provider]
  const { data, isPending, isError } = useRecoverySummary({ window: '7d', provider, namespaceId, environment })

  const parts = ORDER.map((s) => ({ state: s, count: data?.states.find((x) => x.state === s)?.count ?? 0 })).filter((p) => p.count > 0)
  const total = parts.reduce((n, p) => n + p.count, 0)
  const rate = data?.stayedFixedRate

  return (
    <InsightCard title="How replays ended" note="last 7 days">
      {isPending && <p role="status" className="text-[13px] text-[var(--color-text-muted)]">Reading the ledger…</p>}
      {isError && <p role="alert" className="text-[13px] text-[var(--color-text-muted)]">ServiceHub couldn’t read the ledger just now.</p>}
      {data && total === 0 && <p className="text-[13px] text-[var(--color-text-muted)]">Nothing has been replayed in {cloud} in the last 7 days.</p>}
      {data && total > 0 && (
        <>
          <p className="flex items-baseline gap-2">
            {typeof rate === 'number' ? (
              <>
                <span className="tabular text-[30px] font-extrabold leading-none tracking-tight text-[var(--color-text)]">{Math.round(rate * 100)}%</span>
                <span className="text-[12.5px] text-[var(--color-text-muted)]">of proven replays stayed fixed</span>
              </>
            ) : (
              <span className="text-[13px] text-[var(--color-text-muted)]">Nothing has been proven either way yet, so there is no “stayed fixed” rate.</span>
            )}
          </p>

          <div
            role="img"
            aria-label={`${total} recoveries: ${parts.map((p) => `${p.count} ${stateChip[p.state]}`).join(', ')}`}
            className="mt-3 flex h-2.5 gap-0.5 overflow-hidden rounded-full"
          >
            {parts.map((p) => (
              <span key={p.state} title={`${stateChip[p.state]}: ${p.count.toLocaleString()} — ${stateMeaning[p.state]}`} style={{ flexGrow: p.count, background: TONE[stateTone(p.state)].fill }} className="min-w-1" />
            ))}
          </div>

          <ul className="mt-3 space-y-1.5">
            {parts.map((p) => {
              const { Icon, fill } = TONE[stateTone(p.state)]
              return (
                <li key={p.state} title={stateMeaning[p.state]} className="flex items-center gap-2 text-[12.5px] text-[var(--color-text)]">
                  <span aria-hidden="true" className="flex h-4 w-4 items-center justify-center rounded-full text-white" style={{ background: fill }}>
                    <Icon className="h-2.5 w-2.5" strokeWidth={3} />
                  </span>
                  {stateChip[p.state]}
                  <span className="tabular ml-auto font-bold">{p.count.toLocaleString()}</span>
                </li>
              )
            })}
          </ul>
          {parts.some((p) => p.state === 'Unverified') && (
            <p className="mt-2 flex items-start gap-1.5 text-[11.5px] text-[var(--color-text-muted)]">
              <TriangleAlert className="mt-px h-3 w-3 shrink-0" aria-hidden="true" />
              Unverified means {cloud} can’t prove the queue stayed empty — it is not counted as fixed or as failed.
            </p>
          )}
        </>
      )}
    </InsightCard>
  )
}
