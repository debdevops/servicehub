import { Link } from 'react-router-dom'
import { columnHelp } from '../content/columns'
import { InfoTip } from './ui/InfoTip'
import type { CloudSummary } from '../lib/homeSummary'
import type { EntityKind, Namespace } from '../lib/api/namespaces'

const kindWords: Record<EntityKind, [string, string]> = {
  queue: ['queue', 'queues'],
  topic: ['topic', 'topics'],
  subscription: ['subscription', 'subscriptions'],
}

type Tone = 'ok' | 'warn' | 'err'
const chipClass: Record<Tone, string> = {
  ok: 'bg-[var(--color-success-light)] text-[#047857]',
  warn: 'bg-[var(--color-warning-light)] text-[#92400e]',
  err: 'bg-[var(--color-error-light)] text-[#b91c1c]',
}

/**
 * "{Cloud} at a glance" — only the kinds of thing this cloud actually has, each with a number and one
 * chip in words ("8 with dead letters"), never the abbreviation. A chip is left off where the cloud
 * cannot count per entity: silence is not "Healthy" (R5). The link goes to Fleet Overview, which exists
 * only with two clouds, so the page passes `fleetHref` only then.
 */
export function FleetCard({
  cloud,
  summary,
  namespaces,
  fleetHref,
}: {
  cloud: string
  summary: CloudSummary
  namespaces: readonly Namespace[]
  fleetHref?: string
}) {
  const needALook = namespaces.filter((n) => n.lastConnectionTestSucceeded === false).length
  const cells: { key: string; label: string; value: number; chip: { text: string; tone: Tone } | null }[] = [
    {
      key: 'namespaces',
      label: 'Namespaces',
      value: namespaces.length,
      chip: needALook > 0 ? { text: `${needALook} needs a look`, tone: 'warn' } : null,
    },
    ...summary.byKind.map(({ kind, count, withDeadLetters }) => ({
      key: kind,
      label: kindWords[kind][1].replace(/^./, (c) => c.toUpperCase()),
      value: count,
      chip:
        withDeadLetters === null
          ? null
          : withDeadLetters > 0
            ? { text: `${withDeadLetters.toLocaleString()} with dead letters`, tone: 'err' as Tone }
            : { text: 'Healthy', tone: 'ok' as Tone },
    })),
  ]

  return (
    <section aria-label={`${cloud} at a glance`} className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between border-b border-[#f3f4f6] px-4 py-[13px]">
        <h2 className="text-[13.5px] font-bold text-[#1f2937]">{cloud} at a glance</h2>
        {fleetHref && (
          <Link to={fleetHref} className="text-[11.5px] font-semibold text-[var(--color-primary-600)] hover:underline">
            Compare with your other clouds →
          </Link>
        )}
      </div>
      {summary.byKind.length === 0 ? (
        <p className="px-4 py-3 text-sm text-[var(--color-text-muted)]">No queues or topics were found in this cloud yet.</p>
      ) : (
        <ul className="grid grid-cols-2 gap-px bg-[#f3f4f6]">
          {cells.map((c) => (
            <li key={c.key} className="bg-[var(--color-surface)] px-[15px] py-[13px]">
              <div className="flex items-center text-[11px] font-semibold text-[var(--color-text-muted)]">{c.label}{columnHelp.glance[c.key as keyof typeof columnHelp.glance] && <InfoTip help={columnHelp.glance[(c.key === 'queue' ? 'queues' : c.key === 'topic' ? 'topics' : c.key === 'subscription' ? 'subscriptions' : c.key) as keyof typeof columnHelp.glance]} />}</div>
              <div className="tabular mt-px text-[22px] font-extrabold leading-[1.15] tracking-tight">
                <span className="sr-only">{c.value.toLocaleString()} {kindWords[c.key as EntityKind]?.[c.value === 1 ? 0 : 1] ?? 'namespaces'}</span>
                <span aria-hidden="true">{c.value.toLocaleString()}</span>
              </div>
              {c.chip && (
                <span className={`mt-[5px] inline-block rounded-full px-[7px] py-px text-[10px] font-bold ${chipClass[c.chip.tone]}`}>{c.chip.text}</span>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
