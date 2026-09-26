import { Link } from 'react-router-dom'
import { useDeadLetters } from '../../hooks/useDeadLetters'
import type { CloudProvider, EnvironmentKind } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'
import { NO_REASON } from '../message/FailureGroups'
import { InsightCard } from './InsightCard'

// One series, so one hue: dead letters are the app's red. Everything else is ink and track grey.
const BAR = '#dc2626'
const TRACK = '#f3f4f6'
const SHOWN = 6

/**
 * Why the dead letters are dead: one bar per RECORDED reason, biggest first. The reasons are what the cloud or the application
 * wrote down — never a category ServiceHub guessed — so on a cloud that records none, the honest bar is "No reason recorded".
 * It reads the same list Dead letters does, so the counts here are the counts there. The same card sits on every cloud.
 */
export function WhyMessagesFailed({
  provider,
  namespaceId,
  environment,
  scopeQuery,
}: {
  provider: CloudProvider
  namespaceId?: string
  environment?: EnvironmentKind
  /** `&ns=…` / `&env=…` for the links, so opening a reason keeps the scope. */
  scopeQuery: string
}) {
  const cloud = providerLabel[provider]
  const { data, isPending, isError } = useDeadLetters({ provider, namespaceId, environment, status: 'active', pageSize: 1 })

  const groups = [...(data?.groups ?? [])].sort((a, b) => b.count - a.count)
  const shown = groups.slice(0, SHOWN)
  const folded = groups.slice(SHOWN).reduce((n, g) => n + g.count, 0) + (data?.otherReasons?.count ?? 0)
  const total = groups.reduce((n, g) => n + g.count, 0) + (data?.otherReasons?.count ?? 0)
  const max = Math.max(1, ...shown.map((g) => g.count))

  return (
    <InsightCard title="Why messages failed" note={total > 0 ? `${total.toLocaleString()} dead-lettered` : undefined}>
      {isPending && <p role="status" className="text-[13px] text-[var(--color-text-muted)]">Reading {cloud}…</p>}
      {isError && <p role="alert" className="text-[13px] text-[var(--color-text-muted)]">ServiceHub couldn’t read the reasons just now.</p>}
      {data && total === 0 && <p className="text-[13px] text-[var(--color-text-muted)]">Nothing is dead-lettered in {cloud} right now, so there is nothing to explain.</p>}
      {data && total > 0 && (
        <ul className="space-y-3">
          {shown.map((g) => {
            const label = g.reason ?? 'No reason recorded'
            const pct = Math.round((g.count / total) * 100)
            return (
              <li key={g.reason ?? NO_REASON}>
                <Link
                  to={`/?tab=dlq&reason=${encodeURIComponent(g.reason ?? NO_REASON)}${scopeQuery}`}
                  title={`${label}: ${g.count.toLocaleString()} (${pct}%). Open these dead letters.`}
                  className="group block rounded-md outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-primary-400)]"
                >
                  <span className="flex items-baseline gap-2 text-[12.5px] text-[var(--color-text)]">
                    <span className="min-w-0 truncate group-hover:underline">{label}</span>
                    <span className="tabular ml-auto font-bold">{g.count.toLocaleString()}</span>
                    <span className="w-9 text-right text-[11px] text-[var(--color-text-muted)]">{pct}%</span>
                  </span>
                  <span className="mt-1 block h-1.5 rounded-full" style={{ background: TRACK }} aria-hidden="true">
                    <span className="block h-1.5 rounded-full" style={{ width: `${(g.count / max) * 100}%`, background: BAR }} />
                  </span>
                </Link>
              </li>
            )
          })}
          {folded > 0 && <li className="text-[12px] text-[var(--color-text-muted)]">+ {folded.toLocaleString()} more across other reasons</li>}
        </ul>
      )}
    </InsightCard>
  )
}
