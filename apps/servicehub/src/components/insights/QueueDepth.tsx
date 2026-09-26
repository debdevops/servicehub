import { Link } from 'react-router-dom'
import { namespaceTag } from '../provider/scopeChoice'
import type { Namespace } from '../../lib/api/namespaces'
import { describeEntity } from '../../lib/entities'
import type { CloudSummary } from '../../lib/homeSummary'
import { InsightCard } from './InsightCard'

// Two series on ONE axis (both are message counts, one shared scale), told apart by hue, by the legend, and by position:
// waiting first, dead-lettered second. Blue and red are the app's own "active" and "dead letter" hues (see the stat tiles).
const ACTIVE = '#0284c7'
const DEAD = '#dc2626'
const TRACK = '#f3f4f6'
const SHOWN = 8

type Row = CloudSummary['entities'][number]
const total = ({ entity: e }: Row) => (e.activeMessages ?? 0) + (e.deadLetterMessages ?? 0)

/**
 * Queue depth: how much is waiting and how much is dead-lettered, per queue or subscription, on one shared scale so the bars
 * compare. Built from entity counts alone, so it exists on every cloud that reports counts; on one that does not, it says so
 * rather than drawing zeros (R5). Topics are left out — a topic holds nothing, its subscriptions do. Each row opens that
 * queue's dead letters in that namespace, and once several namespaces are in scope each queue is named with its own.
 */
export function QueueDepth({ summary, namespaces, cloud }: { summary: CloudSummary; namespaces: readonly Namespace[]; cloud: string }) {
  const nameOf = new Map(namespaces.map((n) => [n.id, namespaceTag(n)]))
  const several = namespaces.length > 1
  const counted = summary.entities.filter((r) => r.entity.kind !== 'topic')
  const canCount = summary.active !== null || summary.deadLetters !== null
  const rows = counted.filter((e) => total(e) > 0).sort((a, b) => total(b) - total(a))
  const shown = rows.slice(0, SHOWN)
  const max = Math.max(1, ...shown.flatMap((r) => [r.entity.activeMessages ?? 0, r.entity.deadLetterMessages ?? 0]))

  return (
    <InsightCard title="Queue depth" note={rows.length > 0 ? `${shown.length} of ${rows.length} with messages` : undefined}>
      {!canCount ? (
        <p className="text-[13px] text-[var(--color-text-muted)]">{cloud} does not report message counts, so ServiceHub can’t chart how deep its queues are.</p>
      ) : shown.length === 0 ? (
        <p className="text-[13px] text-[var(--color-text-muted)]">Every queue in {cloud} is empty right now.</p>
      ) : (
        <>
          <p className="mb-3 flex items-center gap-4 text-[11.5px] text-[var(--color-text-muted)]">
            <span className="flex items-center gap-1.5"><span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: ACTIVE }} /> Waiting</span>
            <span className="flex items-center gap-1.5"><span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: DEAD }} /> Dead-lettered</span>
          </p>
          <ul className="space-y-3.5">
            {shown.map((r) => {
              const e = r.entity
              const d = describeEntity(e.name, e.kind)
              const entityLabel = d.kind === 'subscription' && d.topic ? `${d.topic} › ${d.name}` : d.name
              const label = several ? `${nameOf.get(r.namespaceId) ?? 'namespace'} / ${entityLabel}` : entityLabel
              const bars = [
                { key: 'a', name: 'Waiting', value: e.activeMessages, fill: ACTIVE },
                { key: 'd', name: 'Dead-lettered', value: e.deadLetterMessages, fill: DEAD },
              ]
              return (
                <li key={`${r.namespaceId}|${e.kind}|${e.name}`}>
                  <Link
                    to={`/?tab=dlq&entity=${encodeURIComponent(e.name)}&ns=${encodeURIComponent(r.namespaceId)}`}
                    title={`${label} — ${e.activeMessages ?? 'can’t count'} waiting, ${e.deadLetterMessages ?? 'can’t count'} dead-lettered. Open its dead letters.`}
                    className="group block rounded-md outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-primary-400)]"
                  >
                    <span className="block break-all font-mono text-[12px] font-medium text-[var(--color-text)] group-hover:underline">{label}</span>
                    {bars.map((b) => (
                      <span key={b.key} className="mt-1 flex items-center gap-2">
                        <span className="block h-1.5 flex-1 rounded-full" style={{ background: TRACK }} aria-hidden="true">
                          <span className="block h-1.5 rounded-full" style={{ width: `${((b.value ?? 0) / max) * 100}%`, background: b.fill }} />
                        </span>
                        <span className="tabular w-14 text-right text-[11.5px] font-bold text-[var(--color-text)]">
                          {b.value === null ? <span className="font-normal text-[var(--color-text-muted)]">—</span> : b.value.toLocaleString()}
                        </span>
                        <span className="sr-only">{b.name}</span>
                      </span>
                    ))}
                  </Link>
                </li>
              )
            })}
          </ul>
        </>
      )}
    </InsightCard>
  )
}
