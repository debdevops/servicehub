import { ChevronRight, Info, RefreshCw } from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { FailedRunsTab } from '../components/detail/FailedRunsTab'
import { Pager } from '../../components/ui/Pager'
import { useComponentSource } from '../../hooks/useComponentSource'
import { useNamespace, useNamespaces } from '../../hooks/useNamespaces'
import { usePagedList } from '../../hooks/usePagedList'
import { toProblem } from '../../lib/api/client'
import type { CloudKind, CoverageRow } from '../../lib/api/coverage'
import { NotFoundPage } from '../../pages/NotFoundPage'
import { cloudLabel } from '../lib/clouds'
import { credentialWords } from '../lib/words'
import { nextEntry, nextHref } from '../nav/navigation'
import { Th, Tip } from '../components/Tip'

const td = 'px-3 py-3 align-top text-sm'
const panel = 'rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]'
const tabs = [{ value: 'failed', label: 'failed' }, { value: 'access', label: 'Access' }] as const

/** The "not read yet" and "not enabled" words, one place, so the list and the page cannot say different things. */
const NOT_ENABLED = 'Not enabled'

/**
 * One connection of a component that is not a message store: Step Functions, and later Workflows, Logic Apps, Event Grid
 * (PLAN-PLUG-AND-PLAY-COMPONENTS §3.3). The tabs come from the catalogue row's `detection`, not from the component's name: a `runs`
 * component has Failed runs and Access, and nothing here is written for one service. A connection that cannot read the component
 * says why, with the permissions it lacks, rather than showing an empty list that reads as "nothing is wrong".
 */
export function RunsComponentPage({ cloud, row, namespaceId }: { cloud: CloudKind; row: CoverageRow; namespaceId: string }) {
  const [search] = useSearchParams()
  const client = useQueryClient()
  const ns = useNamespace(namespaceId)
  const source = useComponentSource(namespaceId, row.componentKey)
  const tab = search.get('tab') === 'access' ? 'access' : 'failed'
  const cloudName = cloudLabel[cloud]
  const here = nextHref(nextEntry('component-detail'), { cloud, component: row.componentKey, namespaceId })

  if (ns.isError || source.isError) {
    const status = toProblem(ns.error ?? source.error).status
    return status === 404
      ? <NotFoundPage />
      : <p role="alert" className="m-6 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">This connection could not be read. Nothing is shown rather than a list that may be wrong.</p>
  }
  if (ns.isPending || source.isPending) return <p role="status" className="px-6 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
  if (ns.data.provider !== cloud) return <NotFoundPage />

  const place = ns.data.awsRegion ?? ns.data.gcpProjectId ?? null
  const name = ns.data.displayName ?? ns.data.name

  return (
    <div className="space-y-4 px-6 py-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <nav aria-label="Breadcrumb" className="mb-1 flex flex-wrap items-center gap-1 text-sm text-[var(--color-text-muted)]">
            <Link to={nextHref(nextEntry('home'), { cloud })} className="hover:underline">{cloudName}</Link>
            <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
            <Link to={nextHref(nextEntry('component-list'), { cloud, component: row.componentKey })} className="hover:underline">{row.name}</Link>
            <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
            <span aria-current="page" className="font-semibold text-[var(--color-primary-700)]">{name}</span>
          </nav>
          <h1 className="flex flex-wrap items-center gap-3 text-2xl font-bold">{name}
            <span className={`rounded-md px-2 py-0.5 text-xs font-semibold ${source.data.available ? 'bg-green-50 text-green-800' : 'bg-amber-50 text-amber-800'}`}>{source.data.available ? 'Readable' : NOT_ENABLED}</span>
            <Tip label="page:Component detail" />
          </h1>
          <p className="text-sm text-[var(--color-text-muted)]">{row.name}{place ? ` · ${place}` : ''} · <span className="capitalize">{ns.data.environment}</span></p>
        </div>
        <div className="flex gap-3">
          <aside aria-label="What you’re looking at" className="flex max-w-xl gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-3 text-sm">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
            <div><p className="font-semibold">What you’re looking at</p><p>The {row.stuckPlace.toLowerCase()} of {row.name} on this connection. ServiceHub reads them and tells you; it does not run anything again.</p></div>
          </aside>
          <button type="button" aria-label="Refresh" onClick={() => void client.invalidateQueries({ queryKey: ['component-source'] })} className="self-start rounded-md border border-[var(--color-border)] bg-[var(--color-surface)] p-2.5 hover:bg-[var(--color-surface-muted)]"><RefreshCw className="h-4 w-4" aria-hidden="true" /></button>
        </div>
      </div>

      <nav aria-label="Sections of this connection" className="flex flex-wrap items-center gap-1 border-b border-[var(--color-border)]">
        {tabs.map((t) => (
          <Link key={t.value} to={t.value === 'failed' ? here : `${here}?tab=${t.value}`} aria-current={t.value === tab ? 'page' : undefined}
            className={`-mb-px border-b-2 px-3 py-2.5 text-sm font-semibold ${t.value === tab ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)] hover:text-[var(--color-text)]'}`}>
            {t.value === 'failed' ? row.stuckPlace : t.label}
          </Link>
        ))}
        <Tip label="Tabs" />
      </nav>

      {tab === 'access' ? (
        <section aria-labelledby="access-title" className={`${panel} p-4`}>
          <h2 id="access-title" className="mb-2 text-base font-semibold">Access<Tip label="section:Component access" /></h2>
          <dl className="text-sm">
            <div className="flex gap-4 border-b border-[var(--color-border)] py-2"><dt className="w-40 shrink-0 text-[var(--color-text-muted)]">Signs in with</dt><dd className="font-semibold">{credentialWords(ns.data.authType)}</dd></div>
            <div className="flex gap-4 py-2"><dt className="w-40 shrink-0 text-[var(--color-text-muted)]">Can read {row.name}</dt><dd className="font-semibold">{source.data.available ? 'Yes' : source.data.reason}</dd></div>
          </dl>
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">ServiceHub asks for read permissions only. It never starts, stops or changes anything here.</p>
        </section>
      ) : source.data.available ? (
        <FailedRunsTab namespaceId={namespaceId} componentKey={row.componentKey} source={source.data} componentName={row.name} />
      ) : (
        <section aria-labelledby="not-enabled-title" role="status" className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
          <h2 id="not-enabled-title" className="mb-1 text-base font-semibold">{NOT_ENABLED}<Tip label="section:Not enabled" /></h2>
          <p>{source.data.reason}</p>
        </section>
      )}
    </div>
  )
}

/**
 * The connections of one `runs` component on one cloud (the list page for a component that is not a message store). It lists the
 * cloud's own connections; each is asked on its own page whether its credential may read the component, so nothing is guessed here.
 */
export function RunsConnections({ cloud, row }: { cloud: CloudKind; row: CoverageRow }) {
  const namespaces = useNamespaces()
  const mine = (namespaces.data ?? []).filter((n) => n.provider === cloud && n.isActive)
  const { items, pager } = usePagedList(mine)
  const cloudName = cloudLabel[cloud]

  return (
    <div className="space-y-4 px-6 py-5">
      <div>
        <nav aria-label="Breadcrumb" className="mb-1 flex items-center gap-1 text-sm text-[var(--color-text-muted)]">
          <Link to={nextHref(nextEntry('home'), { cloud })} className="hover:underline">{cloudName}</Link>
          <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
          <span aria-current="page" className="font-semibold text-[var(--color-primary-700)]">{row.name}</span>
        </nav>
        <h1 className="text-2xl font-bold">{row.name}<Tip label="page:Component" /></h1>
        <p className="text-sm text-[var(--color-text-muted)]">{row.stuckPlace}. {row.statusWords}. {row.note ?? ''}</p>
      </div>
      <section aria-labelledby="runs-connections-title" className={panel}>
        <h2 id="runs-connections-title" className="px-4 py-3 text-base font-semibold">{row.name} connections ({mine.length})<Tip label="section:Run connections" /></h2>
        {namespaces.isError ? (
          <p role="alert" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-red-800">The connections could not be read.</p>
        ) : namespaces.isPending ? (
          <p role="status" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
        ) : mine.length === 0 ? (
          <p className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">You have no connection on {cloudName} yet. Connect one first; {row.name} then uses the same connection.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-t border-[var(--color-border)]">
              <caption className="sr-only">Connections on {cloudName} that may hold {row.name}</caption>
              <thead><tr><Th>Connection</Th><Th>Region / project</Th><Th>Environment</Th><Th>Actions</Th></tr></thead>
              <tbody>
                {items.map((n) => (
                  <tr key={n.id} className="border-t border-[var(--color-border)]">
                    <th scope="row" className={`${td} text-left font-semibold`}>{n.displayName ?? n.name}</th>
                    <td className={td}>{n.awsRegion ?? n.gcpProjectId ?? '—'}</td>
                    <td className={`${td} capitalize`}>{n.environment}</td>
                    <td className={td}><Link to={nextHref(nextEntry('component-detail'), { cloud, component: row.componentKey, namespaceId: n.id })} aria-label={`View ${n.displayName ?? n.name}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">View</Link></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pager {...pager} label={`${row.name} connections`} />
      </section>
    </div>
  )
}
