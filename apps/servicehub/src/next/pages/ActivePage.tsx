import { useQueries, useQueryClient } from '@tanstack/react-query'
import { Boxes, Inbox } from 'lucide-react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { Browser, type Row } from '../../components/message/ActiveMessagesTab'
import { Pager } from '../../components/ui/Pager'
import { useCoverage } from '../../hooks/useCoverage'
import { useComponents } from '../../hooks/useComponents'
import { namespaceKeys, useNamespaces } from '../../hooks/useNamespaces'
import { usePagedList } from '../../hooks/usePagedList'
import type { ConnectedComponent, Count } from '../../lib/api/components'
import { fetchEntities, type Entity, type Namespace } from '../../lib/api/namespaces'
import { formatWhen } from '../../lib/format'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { entityLabel } from '../lib/entityRows'
import { countWords, homeTotals } from '../lib/words'
import { nextEntry, nextHref } from '../nav/navigation'

const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'
const n = (v: number) => v.toLocaleString('en-US')

/** Only places that hold messages waiting for a consumer: a topic holds none itself. */
const holdsMessages = (kind: string) => kind === 'queue' || kind === 'subscription'
const fullPath = (e: { name: string; parent: string | null }) => (e.parent ? `${e.parent}/subscriptions/${e.name}` : e.name)
const keyOf = (namespaceId: string, path: string) => `${namespaceId}|${path}`

interface Line {
  readonly c: ConnectedComponent
  readonly path: string
  readonly label: string
  readonly kind: string
  readonly active: Count
  readonly deadLetters: Count
  readonly observedAtUtc: string | null
  readonly browsable: boolean
}

/**
 * Active messages (build-plan unit E3; design `p08-active-messages`): what has NOT failed — still waiting in a queue or subscription —
 * per place, with counts that say how far they can be trusted. Browsing a place opens the 4.2.0 browser (the same peek, filters,
 * Follow live and Scheduled) below the table, and only where the connection's capability says a look changes nothing
 * (`supportsRepeatablePeek`); where it does not, the row says so and no peek is ever made. Nothing here can be replayed.
 */
export default function ActivePage() {
  const scope = parseScope(useParams().cloud)
  const cloud = cloudOf(scope)
  const { pathname } = useLocation()
  const [params, setParams] = useSearchParams()
  const client = useQueryClient()
  const components = useComponents(cloud)
  const namespaces = useNamespaces()
  const coverage = useCoverage()

  const comps = (components.data?.components ?? []).filter((c) => c.entities.some((e) => holdsMessages(e.kind)))
  const nsById = new Map((namespaces.data ?? []).map((ns) => [ns.id, ns]))
  const nsIds = [...new Set(comps.map((c) => c.namespaceId))]
  const lists = useQueries({ queries: nsIds.map((id) => ({ queryKey: namespaceKeys.entities(id), queryFn: () => fetchEntities(id) })) })
  const listOf = new Map(nsIds.map((id, i) => [id, lists[i]]))

  const lines: Line[] = comps.flatMap((c) => {
    const ns = nsById.get(c.namespaceId)
    const caps = ns?.capabilities
    const counts = listOf.get(c.namespaceId)?.data
    return c.entities.filter((e) => holdsMessages(e.kind)).map((e): Line => {
      const live = counts?.entities.find((x) => x.name === fullPath(e))
      // A cloud that cannot count says so; a count it could not read is not a zero.
      const active: Count = !caps?.supportsMessageCounts ? { value: null, kind: 'notReported' }
        : live?.activeMessages == null ? { value: null, kind: 'notRead' }
        : { value: live.activeMessages, kind: caps.countsAreApproximate ? 'approximate' : 'exact' }
      return { c, path: fullPath(e), label: entityLabel(e.name, e.parent), kind: e.kind, active, deadLetters: e.deadLetters, observedAtUtc: e.observedAtUtc, browsable: caps?.supportsRepeatablePeek === true }
    })
  })
  const { items: shown, pager } = usePagedList(lines)

  const clouds = [...new Set(comps.map((c) => c.cloud))]
  const activeTile = (k: (typeof clouds)[number]) => {
    const mine = lines.filter((l) => l.c.cloud === k)
    const counted = mine.filter((l) => l.active.value !== null)
    return { cloud: k, value: counted.length ? counted.reduce((s, l) => s + (l.active.value ?? 0), 0) : null, approx: counted.some((l) => l.active.kind === 'approximate'), places: mine.length }
  }

  // Browse: every browsable place in scope (a look is free only where the capability says so), opened on the chosen one.
  const browseRows: Row[] = lines.filter((l) => l.browsable).flatMap((l) => {
    const ns = nsById.get(l.c.namespaceId)
    const entity: Entity | undefined = listOf.get(l.c.namespaceId)?.data?.entities.find((x) => x.name === l.path)
    return ns && entity ? [{ namespace: ns as Namespace, entity }] : []
  })
  const chosen = params.get('queue')
  const browsing = chosen !== null && browseRows.some((r) => keyOf(r.namespace.id, r.entity.name) === chosen)
  const stopBrowsing = () => setParams((p) => { const next = new URLSearchParams(p); ['queue', 'active', 'view'].forEach((k) => next.delete(k)); return next }, { replace: true })

  // One row per component type per cloud in scope, from the connection's own capabilities — never from the cloud's name.
  const abilities = [...new Map(comps.map((c) => [`${c.cloud}|${c.componentKey}`, c])).values()].map((c) => {
    const caps = nsById.get(c.namespaceId)?.capabilities
    return { key: `${c.cloud}|${c.componentKey}`, name: `${cloudLabel[c.cloud]} ${c.componentName}`, counts: !caps ? null : !caps.supportsMessageCounts ? 'none' : caps.countsAreApproximate ? 'approx' : 'exact', browsing: caps?.supportsRepeatablePeek ?? null }
  })

  const error = components.isError || coverage.isError
  const loading = components.isPending
  const now = new Date()
  const dlHref = (r: Row) => {
    const l = lines.find((x) => x.c.namespaceId === r.namespace.id && x.path === r.entity.name)
    return l ? `${nextHref(nextEntry('component-detail'), { cloud: l.c.cloud, component: l.c.componentKey, namespaceId: l.c.namespaceId })}?tab=dead-letters&entity=${encodeURIComponent(l.path)}` : pathname
  }

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Active messages" updatedAtMs={components.dataUpdatedAt}
        onRefresh={() => { void client.invalidateQueries({ queryKey: ['components'] }); void client.invalidateQueries({ queryKey: ['namespaces'] }); void client.invalidateQueries({ queryKey: ['active-peek'] }) }}
        lookingAt="Messages that have not failed: still waiting in a queue or subscription. Useful for seeing whether work is moving." />

      {error ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">Active messages could not be read. Nothing is shown rather than numbers that may be wrong.</p>
      ) : loading ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : lines.length === 0 ? (
        <p className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-6 text-sm text-[var(--color-text-muted)]">No queue or subscription is connected {scope === 'all' ? '' : `on ${cloudLabel[scope]} `}yet, so there are no active messages to show.</p>
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {clouds.map((k) => {
              const t = activeTile(k)
              return (
                <Tile key={k} icon={<Boxes className="h-5 w-5 text-[var(--color-primary-700)]" aria-hidden="true" />} title={`Active on ${cloudLabel[k]}`}
                  value={t.value === null ? 'Can’t count here' : n(t.value) + (t.approx ? ' (approx)' : '')}
                  note={t.value === null ? `${t.places} ${t.places === 1 ? 'place' : 'places'}, none counted` : `${t.approx ? 'Approximate' : 'Exact'} · waiting to be processed`} />
              )
            })}
            {cloud && (() => {
              const first = homeTotals(comps).first
              return <Tile icon={<Inbox className="h-5 w-5 text-red-600" aria-hidden="true" />} title={first.label === 'In dead-letter now' ? 'In dead-letter now' : 'Dead letters recorded'}
                value={first.value === null ? 'Can’t read' : n(first.value) + (first.approximate ? ' (approx)' : '')} note={`Across ${comps.length} ${comps.length === 1 ? 'component' : 'components'}`} />
            })()}
          </div>

          <section aria-labelledby="by-entity" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            <h2 id="by-entity" className="px-4 pt-4 text-base font-semibold">Active messages by entity</h2>
            <div className="overflow-x-auto">
              <table className="w-full">
                <caption className="sr-only">Active messages by entity</caption>
                <thead>
                  <tr>
                    {scope === 'all' && <th scope="col" className={th}>Cloud</th>}
                    <th scope="col" className={th}>Entity</th><th scope="col" className={th}>Component</th><th scope="col" className={th}>Type</th>
                    <th scope="col" className={th}>Active</th><th scope="col" className={th}>In dead-letter now</th><th scope="col" className={th}>Last observed</th><th scope="col" className={th}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {shown.map((l) => (
                    <tr key={keyOf(l.c.namespaceId, l.path)} className="border-t border-[var(--color-border)]">
                      {scope === 'all' && <td className={td}>{cloudLabel[l.c.cloud]}</td>}
                      <td className={`${td} font-semibold`}>{l.label}</td>
                      <td className={td}>{l.c.name}</td>
                      <td className={`${td} capitalize`}>{l.kind}</td>
                      <td className={td}>{countWords(l.active)}</td>
                      <td className={td}>{countWords(l.deadLetters)}</td>
                      <td className={td}>{l.observedAtUtc ? formatWhen(l.observedAtUtc, now) : '—'}</td>
                      <td className={td}>
                        {l.browsable
                          ? <Link to={`${pathname}?queue=${encodeURIComponent(keyOf(l.c.namespaceId, l.path))}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">Browse</Link>
                          : <span className="text-xs text-[var(--color-text-muted)]">Browsing not offered</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager {...pager} label="active message places" />
            <p className="px-4 pb-4 text-xs text-[var(--color-text-muted)]">Browse opens the messages still in a queue, read-only. It is offered only where looking is safe.</p>
          </section>

          {browsing && (
            <section aria-label="Browse active messages" className="space-y-2">
              <div className="flex items-center justify-between">
                <h2 className="text-base font-semibold">Browsing active messages</h2>
                <button type="button" onClick={stopBrowsing} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-sm font-semibold">Close browser</button>
              </div>
              <Browser rows={browseRows} deadLetterHref={dlHref} />
            </section>
          )}

          <section aria-labelledby="can-show" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            <h2 id="can-show" className="px-4 pt-4 text-base font-semibold">What this page can show on each cloud</h2>
            <div className="overflow-x-auto">
              <table className="w-full">
                <caption className="sr-only">What this page can show on each cloud</caption>
                <thead><tr><th scope="col" className={th}>Cloud</th><th scope="col" className={th}>Counts</th><th scope="col" className={th}>Browsing</th><th scope="col" className={th}>Why</th></tr></thead>
                <tbody>
                  {abilities.map((a) => (
                    <tr key={a.key} className="border-t border-[var(--color-border)]">
                      <td className={td}>{a.name}</td>
                      <td className={td}>{a.counts === 'exact' ? 'Exact' : a.counts === 'approx' ? 'Approximate' : a.counts === 'none' ? 'Can’t count here' : '—'}</td>
                      <td className={td}>{a.browsing === null ? '—' : a.browsing ? 'Yes' : 'No'}</td>
                      <td className={td}>
                        {a.browsing === null ? 'Not read.' : a.browsing
                          ? 'Looking at a message does not change it.'
                          : 'Looking would count as a delivery attempt, which could push a message into dead-letter by itself.'}
                        {a.counts === 'approx' && ' Counts are estimates.'}
                        {a.counts === 'none' && ' The cloud reports no counts.'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="h-3" />
          </section>
        </>
      )}
    </div>
  )
}

function Tile({ icon, title, value, note }: { icon: React.ReactNode; title: string; value: string; note: string }) {
  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <span className="rounded-full bg-[var(--color-surface-muted)] p-2.5">{icon}</span>
      <div><p className="text-sm font-semibold">{title}</p><p className="text-2xl font-bold">{value}</p><p className="text-xs text-[var(--color-text-muted)]">{note}</p></div>
    </div>
  )
}
