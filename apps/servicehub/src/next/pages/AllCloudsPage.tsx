import { useQueryClient } from '@tanstack/react-query'
import { Cloud, Clock, Info, RefreshCw, TriangleAlert } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Pager } from '../../components/ui/Pager'
import { useComponents } from '../../hooks/useComponents'
import { useCoverage } from '../../hooks/useCoverage'
import { useMe } from '../../hooks/useIdentity'
import { usePagedList } from '../../hooks/usePagedList'
import { usePendingWork } from '../../hooks/usePendingWork'
import type { ConnectedComponent } from '../../lib/api/components'
import type { CloudKind } from '../../lib/api/coverage'
import { formatWhen } from '../../lib/format'
import { AttentionTable } from '../components/home/AttentionTable'
import { cloudLabel } from '../lib/clouds'
import { cloudFacts, homeTotals, noTotalSentence } from '../lib/words'
import { nextEntry, nextHref } from '../nav/navigation'
import { Th } from '../components/Tip'
import { Tip } from '../components/Tip'

const ORDER: readonly CloudKind[] = ['azure', 'aws', 'gcp']
const dot = { good: 'bg-green-600', caution: 'bg-amber-500', none: 'bg-slate-300' } as const
const accent: Readonly<Record<CloudKind, string>> = { azure: 'border-t-blue-600', aws: 'border-t-orange-500', gcp: 'border-t-green-600' }

function Fact({ text, good }: { text: string; good: boolean }) {
  return <li className="flex items-center gap-2 text-sm"><span aria-hidden="true" className={`h-2.5 w-2.5 rounded-full ${good ? 'bg-green-600' : 'bg-slate-300'}`} />{text}</li>
}

/** One cloud, in its own terms (build-plan unit C2). Nothing here is added to another cloud's numbers (R5). */
function CloudCard({ cloud, components, covered }: { cloud: CloudKind; components: readonly ConnectedComponent[]; covered: Parameters<typeof cloudFacts>[1] }) {
  const totals = homeTotals(components)
  const facts = cloudFacts(components, covered)
  const { items: shown, pager } = usePagedList(components)
  const first = totals.first
  const sub = first.unreadable ? 'Could not be read' : first.label === 'Dead letters recorded' ? `${cloudLabel[cloud]} reports no counts` : first.approximate ? 'Approximate' : facts.watched.good ? 'Exact, watched automatically' : 'Exact'
  return (
    <section aria-label={cloudLabel[cloud]} className={`rounded-xl border border-t-4 border-[var(--color-border)] bg-[var(--color-surface)] p-4 ${accent[cloud]}`}>
      <div className="flex items-center justify-between">
        <h2 className="flex items-center text-lg font-bold">{cloudLabel[cloud]}<Tip label="section:Cloud card" /></h2>
        <Link to={nextHref(nextEntry('home'), { cloud })} aria-label={`Open ${cloudLabel[cloud]}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">Open</Link>
      </div>
      <div className="mt-3 grid grid-cols-2 gap-4">
        <div>
          <p className="flex items-center text-sm font-semibold">{first.label}<Tip label={first.label} context="tile" /></p>
          <p className="text-3xl font-bold leading-tight" data-testid={`first-${cloud}`}>
            {first.value === null ? <span className="text-lg font-semibold">{first.unreadable ? 'Can’t read' : 'Not looked yet'}</span> : first.value.toLocaleString('en-US')}
            {first.value !== null && first.approximate && <span className="ml-1.5 text-sm font-normal text-[var(--color-text-muted)]">(approx)</span>}
          </p>
          <p className="text-xs text-[var(--color-text-muted)]">{first.recorded !== null ? `${first.recorded.toLocaleString('en-US')} recorded at last look` : sub}</p>
        </div>
        <div>
          <p className="flex items-center text-sm font-semibold">Needs you<Tip label="Needs you" context="tile" /></p>
          <p className="text-3xl font-bold leading-tight text-red-700" data-testid={`needs-${cloud}`}>{totals.needsYou}</p>
          <p className="text-xs text-[var(--color-text-muted)]">approvals: {totals.approvals}, hand-offs: {totals.handOffs}</p>
        </div>
      </div>
      <table className="mt-3 w-full text-sm" aria-label={`${cloudLabel[cloud]} components`}>
        <thead><tr className="bg-[var(--color-surface-muted)] text-left text-xs font-semibold text-[var(--color-text-muted)]"><Th>Component</Th><Th>Needs you</Th></tr></thead>
        <tbody>
          {shown.map((c) => (
            <tr key={`${c.namespaceId}:${c.componentKey}`} className="border-t border-[var(--color-border)]">
              <td className="px-2 py-2"><span className="font-semibold">{c.name}</span> <span className="text-[var(--color-text-muted)]">{c.componentName}</span>{!c.reachable && <span className="ml-2 text-xs font-semibold text-amber-800">Can’t reach</span>}</td>
              <td className="px-2 py-2 text-right font-semibold text-red-700">{c.needsYou}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <Pager {...pager} label={`${cloudLabel[cloud]} components`} />
      <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5">
        <Fact {...facts.watched} />
        <Fact {...facts.counts} />
        <li className="flex items-center gap-2 text-sm"><span aria-hidden="true" className={`h-2.5 w-2.5 rounded-full ${dot[facts.evidence.tone]}`} />{facts.evidence.text}</li>
      </ul>
    </section>
  )
}

/**
 * Home for every connected cloud (build-plan unit C2; design `p02-all-clouds-home`). Each cloud keeps its own numbers: a dead-letter
 * total across clouds would add exact to approximate to "reports none", so none is drawn and the page says why. What may be summed is
 * ServiceHub's own count of items waiting for a person.
 */
export default function AllCloudsPage() {
  const client = useQueryClient()
  const components = useComponents(null)
  const pending = usePendingWork({})
  const coverage = useCoverage()
  const me = useMe()
  const rows = components.data?.components ?? []
  const clouds = ORDER.map((cloud) => ({ cloud, name: cloudLabel[cloud], components: rows.filter((c) => c.cloud === cloud) })).filter((c) => c.components.length > 0)
  const totals = homeTotals(rows)
  const reason = noTotalSentence(clouds)
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ['components'] })
    void client.invalidateQueries({ queryKey: ['pending-work'] })
  }

  return (
    <div className="space-y-4 px-6 py-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <h1 className="text-2xl font-bold">All clouds<Tip label="page:All clouds" /></h1>
        <div className="flex gap-3">
          <aside aria-label="What you’re looking at" className="flex max-w-xl gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-3 text-sm">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
            <div><p className="font-semibold">What you’re looking at</p><p>Every connected cloud side by side, each in its own terms, and everything waiting for a person. Pick a cloud to work in it.</p></div>
          </aside>
          <div className="flex items-start gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-sm">
            <div><p className="text-[var(--color-text-muted)]">Last updated</p><p className="font-semibold">{components.data ? formatWhen(components.data.generatedAtUtc, new Date()) : '—'}</p></div>
            <button type="button" onClick={refresh} aria-label="Refresh" className="rounded-md p-1.5 hover:bg-[var(--color-surface-muted)]"><RefreshCw className="h-4 w-4" aria-hidden="true" /></button>
          </div>
        </div>
      </div>

      {components.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">The connected components could not be read. Nothing is shown rather than numbers that may be wrong.</p>
      ) : components.isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : (
        <>
          <div className="grid gap-3 md:grid-cols-3">
            <section aria-label="Needs your attention" className="flex items-start gap-4 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
              <span className="mt-1 grid h-10 w-10 place-items-center rounded-full bg-red-50" aria-hidden="true"><TriangleAlert className="h-5 w-5 text-red-600" /></span>
              <div><h2 className="flex items-center text-sm font-semibold">Needs your attention<Tip label="Needs your attention" /></h2><p className="text-3xl font-bold leading-tight" data-testid="all-needs-you">{totals.needsYou}</p>
                <p className="text-xs text-[var(--color-text-muted)]">(approvals: {totals.approvals}, hand-offs: {totals.handOffs}) · across {clouds.length} {clouds.length === 1 ? 'cloud' : 'clouds'}</p></div>
            </section>
            <section aria-label="Pending approvals" className="flex items-start gap-4 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
              <span className="mt-1 grid h-10 w-10 place-items-center rounded-full bg-[var(--color-primary-50)]" aria-hidden="true"><Clock className="h-5 w-5 text-[var(--color-primary-700)]" /></span>
              <div><h2 className="flex items-center text-sm font-semibold">Pending approvals<Tip label="Pending approvals" /></h2><p className="text-3xl font-bold leading-tight" data-testid="all-approvals">{totals.approvals}</p><p className="text-xs text-[var(--color-text-muted)]">Require human approval</p></div>
            </section>
            <section aria-label="Connected" className="flex items-start gap-4 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
              <span className="mt-1 grid h-10 w-10 place-items-center rounded-full bg-[var(--color-primary-50)]" aria-hidden="true"><Cloud className="h-5 w-5 text-[var(--color-primary-700)]" /></span>
              <div><h2 className="flex items-center text-sm font-semibold">Connected<Tip label="Connected clouds" /></h2><p className="text-3xl font-bold leading-tight" data-testid="all-connected">{clouds.length} <span className="text-base font-semibold">{clouds.length === 1 ? 'cloud' : 'clouds'}</span></p><p className="text-xs text-[var(--color-text-muted)]">{rows.length} {rows.length === 1 ? 'component' : 'components'}</p></div>
            </section>
          </div>

          {reason && <p role="note" className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-2.5 text-sm"><strong>No total across clouds.</strong> {reason}</p>}

          {clouds.length === 0 ? (
            <p className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-6 text-sm text-[var(--color-text-muted)]">No cloud is connected yet. Pick a cloud in the menu, then use <strong>Connect a cloud</strong>.</p>
          ) : (
            <div className="grid gap-4 lg:grid-cols-3">
              {clouds.map(({ cloud, components: mine }) => (
                <CloudCard key={cloud} cloud={cloud} components={mine} covered={(coverage.data?.rows ?? []).filter((r) => r.cloud === cloud && mine.some((c) => c.componentKey === r.componentKey))} />
              ))}
            </div>
          )}
        </>
      )}

      {pending.isError
        ? <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">What needs your attention could not be read.</p>
        : <AttentionTable items={pending.data?.items ?? []} coverage={coverage.data?.rows ?? []} me={me.data} showCloud />}
    </div>
  )
}
