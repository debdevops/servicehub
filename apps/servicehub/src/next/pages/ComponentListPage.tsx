import { useQueryClient } from '@tanstack/react-query'
import { ChevronRight, Info, Plus, RefreshCw } from 'lucide-react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { Pager } from '../../components/ui/Pager'
import { useComponents } from '../../hooks/useComponents'
import { useCoverage } from '../../hooks/useCoverage'
import { useNamespaces } from '../../hooks/useNamespaces'
import { usePagedList } from '../../hooks/usePagedList'
import type { ConnectedComponent } from '../../lib/api/components'
import type { CloudKind } from '../../lib/api/coverage'
import type { Namespace } from '../../lib/api/namespaces'
import { formatWhen } from '../../lib/format'
import { NotFoundPage } from '../../pages/NotFoundPage'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { countWords, statusWords } from '../lib/words'
import { nextEntry, nextHref } from '../nav/navigation'
import { Th } from '../components/Tip'
import { Tip } from '../components/Tip'
import { RunsConnections } from './RunsComponentPage'

const td = 'px-3 py-3 align-top text-sm'
const tone = { bad: 'bg-red-50 text-red-700', warn: 'bg-amber-50 text-amber-800', ok: 'bg-green-50 text-green-800' } as const

/** Where a connection lives, from what is stored: an AWS region or a Google project. Azure stores neither, so it has no such column. */
const placeOf = (ns: Namespace | undefined) => ns?.awsRegion ?? ns?.gcpProjectId ?? null

/**
 * Every connection of one component on one cloud (build-plan unit D1; design `p04-azure-service-bus-list`), from the same read as
 * Home (B1) filtered by component, so the two cannot disagree. Component and cloud come from the address; a component the
 * catalogue does not list as one ServiceHub reads is not a page.
 */
export default function ComponentListPage() {
  const params = useParams()
  const cloud = cloudOf(parseScope(params.cloud))
  const coverage = useCoverage()
  if (cloud === null || !params.component) return <NotFoundPage />
  if (coverage.isPending) return <p role="status" className="px-6 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
  const runs = coverage.data?.rows.find((r) => r.cloud === cloud && r.componentKey === params.component && r.inSidebar && r.detection === 'runs')
  return runs ? <RunsConnections cloud={cloud} row={runs} /> : <List cloud={cloud} componentKey={params.component} />
}

function List({ cloud, componentKey }: { cloud: CloudKind; componentKey: string }) {
  const { pathname } = useLocation()
  const client = useQueryClient()
  const coverage = useCoverage()
  const components = useComponents(cloud)
  const namespaces = useNamespaces()

  const row = coverage.data?.rows.find((r) => r.cloud === cloud && r.componentKey === componentKey && r.inSidebar)
  const mine = (components.data?.components ?? []).filter((c) => c.componentKey === componentKey)
  const { items, pager } = usePagedList(mine)

  if (coverage.isPending) return <p role="status" className="px-6 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
  if (!row) return <NotFoundPage />

  const cloudName = cloudLabel[cloud]
  const placeById = new Map((namespaces.data ?? []).map((n) => [n.id, placeOf(n)]))
  const showPlace = mine.some((c) => placeById.get(c.namespaceId))
  const anyReadOnly = mine.some((c) => !c.canSend)
  const now = new Date()
  const connect = `${nextHref(nextEntry('connect'), {}, pathname)}&component=${encodeURIComponent(componentKey)}`
  const detail = (c: ConnectedComponent) => nextHref(nextEntry('component-detail'), { cloud, component: componentKey, namespaceId: c.namespaceId })

  return (
    <div className="space-y-4 px-6 py-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <nav aria-label="Breadcrumb" className="mb-1 flex items-center gap-1 text-sm text-[var(--color-text-muted)]">
            <Link to={nextHref(nextEntry('home'), { cloud })} className="hover:underline">{cloudName}</Link>
            <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
            <span aria-current="page" className="font-semibold text-[var(--color-primary-700)]">{row.name}</span>
          </nav>
          <h1 className="text-2xl font-bold">{row.name}<Tip label="page:Component" /></h1>
        </div>
        <div className="flex gap-3">
          <aside aria-label="What you’re looking at" className="flex max-w-xl gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-3 text-sm">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
            <div><p className="font-semibold">What you’re looking at</p><p>Every {row.name} connection you have on {cloudName}. Open one to see its queues, topics and subscriptions.</p></div>
          </aside>
          <div className="flex items-start gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-sm">
            <div><p className="text-[var(--color-text-muted)]">Last updated</p><p className="font-semibold">{components.data ? formatWhen(components.data.generatedAtUtc, now) : '—'}</p></div>
            <button type="button" aria-label="Refresh" onClick={() => void client.invalidateQueries({ queryKey: ['components'] })} className="rounded-md p-1.5 hover:bg-[var(--color-surface-muted)]"><RefreshCw className="h-4 w-4" aria-hidden="true" /></button>
          </div>
        </div>
      </div>

      <section aria-labelledby="list-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
          <h2 id="list-title" className="text-base font-semibold">{row.name} connections ({mine.length})<Tip label="section:Connections table" /></h2>
          <Link to={connect} className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-bold text-white hover:bg-[var(--color-primary-700)]">
            <Plus className="h-4 w-4" aria-hidden="true" /> Connect a {row.name} connection
          </Link>
          <Tip label="Connect a component" />
        </div>

        {components.isError ? (
          <p role="alert" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-red-800">The {row.name} connections could not be read. Nothing is shown rather than numbers that may be wrong.</p>
        ) : components.isPending ? (
          <p role="status" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
        ) : mine.length === 0 ? (
          <p className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">No {row.name} connection is connected yet. Use <strong>Connect a {row.name} connection</strong> to add one.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-t border-[var(--color-border)]">
              <caption className="sr-only">{row.name} connections on {cloudName}</caption>
              <thead>
                <tr>
                  <Th>Connection</Th>
                  {showPlace && <Th>Region / project</Th>}
                  <Th>Environment</Th>
                  <Th>Connection status</Th>
                  <Th>Status</Th>
                  <Th>Queues and subscriptions</Th>
                  <Th>In dead-letter now</Th>
                  <Th>Needs you</Th>
                  <Th>Last observed</Th>
                  <Th>Actions</Th>
                </tr>
              </thead>
              <tbody>
                {items.map((c) => {
                  const status = statusWords(c)
                  return (
                    <tr key={c.namespaceId} className="border-t border-[var(--color-border)]">
                      <th scope="row" className={`${td} text-left font-semibold`}>{c.name}</th>
                      {showPlace && <td className={td}>{placeById.get(c.namespaceId) ?? '—'}</td>}
                      <td className={`${td} capitalize`}>{c.environment}</td>
                      <td className={td}>
                        {c.reachable
                          ? <><span className="font-semibold text-green-700">Connected</span><span className="block text-xs text-[var(--color-text-muted)]">{c.canSend ? 'Read + Send' : 'Read only'}</span></>
                          : <><span className="font-semibold text-amber-800">Can’t reach</span>{c.lastError && <span className="block max-w-[16rem] text-xs text-[var(--color-text-muted)]">{c.lastError}</span>}</>}
                      </td>
                      <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${tone[status.tone]}`}>{status.text}</span></td>
                      <td className={td}>{c.reachable ? c.entities.length.toLocaleString('en-US') : '—'}</td>
                      <td className={td}>{countWords(c.deadLetters)}</td>
                      <td className={`${td} font-semibold ${c.needsYou > 0 ? 'text-red-700' : ''}`}>{c.needsYou}</td>
                      <td className={td}>{c.observedAtUtc ? formatWhen(c.observedAtUtc, now) : '—'}</td>
                      <td className={td}><Link to={detail(c)} aria-label={`View ${c.name}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">View</Link></td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
        {anyReadOnly && <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">A read-only connection shows everything, and offers no Replay until the send right is added.</p>}
        <Pager {...pager} label={`${row.name} connections`} />
      </section>
    </div>
  )
}
