import { Clock, Cloud, Inbox, TriangleAlert } from 'lucide-react'
import { formatWhen } from '../../../lib/format'
import type { HomeTotals } from '../../lib/words'
import { Tip } from '../Tip'

const n = (value: number) => value.toLocaleString('en-US')

function Tile({ icon, tone, title, children }: { icon: React.ReactNode; tone: string; title: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="flex items-start gap-4 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <span className={`mt-1 grid h-10 w-10 shrink-0 place-items-center rounded-full ${tone}`} aria-hidden="true">{icon}</span>
      <div className="min-w-0">
        <h2 className="flex items-center text-sm font-semibold text-[var(--color-text)]">{title}<Tip label={title} context="tile" /></h2>
        {children}
      </div>
    </section>
  )
}

const small = 'text-xs text-[var(--color-text-muted)]'

/**
 * The three tiles (build-plan unit C1). The first says "In dead-letter now" only when a component reports a number; a cloud that
 * reports none says "Dead letters recorded" and shows what ServiceHub kept, so an adapter's 0 is never drawn as a measurement.
 */
export function Tiles({ totals, observedAtUtc, cloudName, reachable }: { totals: HomeTotals; observedAtUtc: string | null; cloudName: string; reachable: number }) {
  const first = totals.first
  const now = new Date()
  return (
    <div className="grid gap-3 md:grid-cols-2 2xl:grid-cols-4">
      <Tile icon={<Inbox className="h-5 w-5 text-[var(--color-primary-700)]" />} tone="bg-[var(--color-primary-50)]" title={first.label}>
        <p className="text-3xl font-bold leading-tight" data-testid="tile-first-value">
          {first.value === null ? <span className="text-lg font-semibold">{first.unreadable ? 'Can’t read' : 'Not looked yet'}</span> : n(first.value)}
          {first.value !== null && first.approximate && <span className="ml-1.5 text-sm font-normal text-[var(--color-text-muted)]">(approx)</span>}
        </p>
        <p className={small}>
          Across {first.components} {first.components === 1 ? 'component' : 'components'}
          {first.recorded !== null && ` · ${n(first.recorded)} recorded at last look`}
        </p>
        {observedAtUtc && (
          <p className={small}>
            {first.label === 'Dead letters recorded' ? 'At your last look' : `Reported by ${cloudName}`}, {formatWhen(observedAtUtc, now)}
          </p>
        )}
      </Tile>
      <Tile icon={<TriangleAlert className="h-5 w-5 text-red-600" />} tone="bg-red-50" title="Needs your attention">
        <p className="text-3xl font-bold leading-tight" data-testid="tile-needs-you">{n(totals.needsYou)}</p>
        <p className={small}>(approvals: {n(totals.approvals)}, hand-offs: {n(totals.handOffs)})</p>
        <p className={small}>Includes approvals + hand-offs to a person</p>
      </Tile>
      <Tile icon={<Clock className="h-5 w-5 text-[var(--color-primary-700)]" />} tone="bg-[var(--color-primary-50)]" title="Pending approvals">
        <p className="text-3xl font-bold leading-tight" data-testid="tile-approvals">{n(totals.approvals)}</p>
        <p className={small}>Require human approval</p>
      </Tile>
      <Tile icon={<Cloud className="h-5 w-5 text-[var(--color-primary-700)]" />} tone="bg-[var(--color-primary-50)]" title="Connected">
        <p className="text-3xl font-bold leading-tight" data-testid="tile-connected">{first.components} <span className="text-base font-semibold">{first.components === 1 ? 'component' : 'components'}</span></p>
        <p className={small}>{reachable} of {first.components} reachable</p>
      </Tile>
    </div>
  )
}
