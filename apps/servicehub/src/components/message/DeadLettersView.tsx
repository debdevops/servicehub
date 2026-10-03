import { useEffect, useState } from 'react'
import { namespaceTag } from '../provider/scopeChoice'
import { usePageSize } from '../../lib/pageSize'
import { Link, useSearchParams } from 'react-router-dom'
import { CheckCircle2, ListRestart, RefreshCw, TriangleAlert } from 'lucide-react'
import { InfoTip } from '../ui/InfoTip'
import { sectionHelp } from '../../content/sections'
import { formatAgo } from '../../lib/format'
import { ReasonStrip } from './ReasonStrip'
import { ExplainerCard, ExplainerToggle } from '../explainer/Explainer'
import { useExplainer } from '../explainer/useExplainer'
import { Pager } from '../ui/Pager'
import { BulkBar } from './BulkBar'
import { EntityPicker } from './EntityPicker'
import { FailureGroups, NO_REASON } from './FailureGroups'
import { LookNow } from './LookNow'
import { MessageTable } from './MessageTable'
import { WorkTabs } from './WorkTabs'
import { useDeadLetters } from '../../hooks/useDeadLetters'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { openReplayAll, useReplayAll } from '../../lib/replayAll'
import type { DeadLetterRange } from '../../lib/api/deadLetters'
import { bulkSelection } from '../../lib/bulkSelection'
import { environmentMeta, resolveScope } from '../provider/scopeChoice'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'
import { Skeleton } from '../ui/Skeleton'
import { Select } from '../ui/Select'
import PageHelpLink from '../help/PageHelpLink'


const ranges: readonly { id: DeadLetterRange; label: string }[] = [
  { id: 'all', label: 'All time' },
  { id: '24h', label: 'Last 24 hours' },
  { id: '7d', label: 'Last 7 days' },
  { id: '30d', label: 'Last 30 days' },
]

const asRange = (v: string | null): DeadLetterRange => (v === '24h' || v === '7d' || v === '30d' ? v : 'all')

/** 4.0.0's DLQ History, as one filter (unit 6.11). `?status=` — absent means stuck now. */
type Showing = 'active' | 'resolved' | 'all'
const showings: readonly { id: Showing; label: string }[] = [
  { id: 'active', label: 'Stuck now' },
  { id: 'resolved', label: 'No longer stuck' },
  { id: 'all', label: 'All' },
]
const asShowing = (v: string | null): Showing => (v === 'resolved' || v === 'all' ? v : 'active')

/**
 * Home's `?tab=dlq`: what is dead-lettered in one cloud, and why — one table, with the failure groups
 * above it (D45: not a page). Every control is in the URL, so a filtered view is a link that survives a
 * refresh.
 */
export function DeadLettersView({ provider, namespaces }: { provider: CloudProvider; namespaces: readonly Namespace[] }) {
  const cloud = providerLabel[provider]
  const [params, setParams] = useSearchParams()
  const explainer = useExplainer('dead-letters')
  // `namespaces` is already narrowed by Home; this only recovers which level of scope that was.
  const scope = resolveScope(namespaces, params)

  const page = Math.max(1, Number(params.get('page')) || 1)
  const [pageSize, setPageSize] = usePageSize()
  const range = asRange(params.get('range'))
  const showing = asShowing(params.get('status'))
  const reasonParam = params.get('reason')
  const entity = params.get('entity') ?? undefined
  const q = params.get('q') ?? ''

  const query = {
    provider,
    // One namespace in scope (`?ns=`) → ask the API for just that one; a whole environment (`?env=`) → for just that environment.
    namespaceId: scope.ns?.id,
    environment: scope.env ?? undefined,
    status: showing,
    range,
    reason: reasonParam && reasonParam !== NO_REASON ? reasonParam : undefined,
    noReason: reasonParam === NO_REASON,
    entity,
    q: q || undefined,
    page,
    pageSize,
  }
  const { data, isPending, isError, refetch, isFetching, dataUpdatedAt } = useDeadLetters(query)
  const mayReplay = permission(useMe().data, 'Operator', 'replay these messages', { recover: true })
  const replayAllRun = useReplayAll()
  const replayAllBusy = replayAllRun?.phase === 'running'
  const scopeLabel = scope.ns ? `${cloud} · ${scope.ns.displayName ?? scope.ns.name}` : scope.env ? `All ${environmentMeta[scope.env].label} namespaces in ${cloud}` : `All namespaces in ${cloud}`

  const change = (patch: Record<string, string | null>) =>
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        for (const [k, v] of Object.entries(patch)) {
          if (v === null || v === '') next.delete(k)
          else next.set(k, v)
        }
        if (!('page' in patch)) next.delete('page') // a new filter starts at the top
        return next
      },
      { replace: true },
    )

  // Search is typed, so it is applied a moment after the last keystroke rather than on each one.
  const [draft, setDraft] = useState(q)
  useEffect(() => {
    if (draft === q) return
    const t = setTimeout(() => change({ q: draft.trim() || null }), 300)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft])

  // Selection belongs to a filter. Change the filter and it is gone — a selection you cannot see is
  // a selection that can be acted on by mistake.
  const signature = [showing, reasonParam, entity, range, q].join('|')
  const [selection, setSelection] = useState<{ sig: string; ids: ReadonlySet<string>; all: boolean }>({ sig: signature, ids: new Set(), all: false })
  const current = selection.sig === signature ? selection : { sig: signature, ids: new Set<string>(), all: false }

  // Whether ServiceHub looks in this cloud on its own. Where it does not (a peek there is a delivery
  // attempt), an empty list is silence, not good news — and must not read as good news (R5).
  const watched = namespaces.every((n) => n.capabilities?.supportsRepeatablePeek === true)
  const names = new Map(namespaces.map((n) => [n.id, namespaceTag(n)]))
  const total = data?.paging.total ?? 0
  const filtering = !!(reasonParam || entity || q || range !== 'all' || showing !== 'active')
  const reasonLabel = reasonParam === NO_REASON ? 'with no reason recorded' : reasonParam
  const groupTotal = (data?.groups ?? []).reduce((n, g) => n + g.count, 0) + (data?.otherReasons?.count ?? 0)

  const rows = data?.items ?? []
  // A bulk run that has ended has used up its selection: untick it, so Replay selected goes back to waiting for a new one.
  useEffect(() => bulkSelection.onFinished(() => setSelection({ sig: signature, ids: new Set(), all: false })), [signature])
  const selectedCount = current.all ? total : current.ids.size

  const toggle = (id: string) => {
    const ids = new Set(current.ids)
    if (ids.has(id)) ids.delete(id)
    else ids.add(id)
    setSelection({ sig: signature, ids, all: false })
  }
  const togglePage = () => {
    const keys = rows.map((r) => String(r.id))
    const everyOnPage = keys.every((k) => current.ids.has(k))
    const ids = new Set(current.ids)
    keys.forEach((k) => (everyOnPage ? ids.delete(k) : ids.add(k)))
    setSelection({ sig: signature, ids, all: false })
  }

  // Both bars are the same control: disabled until something is ticked, and free again once a run has finished and untick what it carried.
  const bulkBar = (edge: 'top' | 'bottom') => (
    <BulkBar
      edge={edge}
      allOnPage={rows.length > 0 && rows.every((r) => current.ids.has(String(r.id)))}
      onTogglePage={togglePage}
      single={!current.all && current.ids.size === 1 ? [...current.ids][0] : null}
      count={selectedCount}
      allMatching={current.all}
      matchingTotal={total}
      matchingLabel={reasonLabel ?? 'dead letters'}
      canSelectAll={!!reasonParam && total > rows.length}
      onSelectAll={() => setSelection({ sig: signature, ids: new Set(rows.map((r) => String(r.id))), all: true })}
      onClear={() => setSelection({ sig: signature, ids: new Set(), all: false })}
      onOpen={() =>
        bulkSelection.set(current.all ? { query: { ...query, page: 1, pageSize: 100 }, total } : { ids: [...current.ids].map(Number) })
      }
    />
  )

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-4 min-h-[76px] flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
        <div>
        <h1 className="flex items-center gap-2 text-2xl font-extrabold tracking-tight text-[var(--color-text)]">
          {cloud} — Dead letters <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} /> <PageHelpLink page="dead-letters" />
        </h1>
        <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">
          Messages that failed too many times and were set aside. Pick one to see why, and put it back.
        </p>
        <p className="mt-1 text-xs text-[var(--color-text-muted)]">
          {scope.ns ? (
            <>Namespace: {scope.ns.displayName ?? scope.ns.name}</>
          ) : scope.env ? (
            <>Namespaces: all {environmentMeta[scope.env].label} in {cloud} ({namespaces.length})</>
          ) : (
            <>
              Namespace: all in {cloud}
              {namespaces.length > 1 ? ` (${namespaces.length})` : ''}
            </>
          )}
        </p>
        </div>
        {data && <ReasonStrip provider={provider} page={data} namespaceId={scope.ns?.id} environment={scope.env ?? undefined} />}
      </header>
      <WorkTabs current="dlq" counts={data ? { dlq: total } : {}} />

      {!watched && <LookNow cloud={cloud} namespaces={namespaces.filter((n) => n.capabilities?.supportsRepeatablePeek !== true)} />}

      {explainer.shown && <ExplainerCard id="dead-letters" onDismiss={explainer.dismiss} />}


      {isPending && <Skeleton label="Reading dead letters…" rows={6} />}

      {isError && (
        <div role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read its list of dead letters.</p>
          <button type="button" onClick={() => void refetch()} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">
            Try again
          </button>
        </div>
      )}

      {data && (
        <>
          <FailureGroups
            groups={data.groups}
            other={data.otherReasons}
            selected={reasonParam}
            onSelect={(reason) => change({ reason })}
          />

          <div className="mb-3 flex flex-wrap items-center gap-3 text-sm">
            <div className="flex items-center gap-2">
              <span className="text-[var(--color-text-muted)]">Showing</span>
              <Select variant="inline" ariaLabel="Showing" value={showing} onChange={(v) => change({ status: v === 'active' ? null : v })}>
                {showings.map((o) => (
                  <option key={o.id} value={o.id}>{o.label}</option>
                ))}
              </Select>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-[var(--color-text-muted)]">Time window</span>
              <Select variant="inline" ariaLabel="Time window" value={range} onChange={(v) => change({ range: v === 'all' ? null : v })}>
                {ranges.map((r) => (
                  <option key={r.id} value={r.id}>{r.label}</option>
                ))}
              </Select>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-[var(--color-text-muted)]">Queue or topic</span>
              <EntityPicker namespaces={namespaces} cloud={cloud} recorded={data.entities} value={entity} onChange={(e) => change({ entity: e })} />
            </div>
            <label className="flex items-center gap-2">
              <span className="sr-only">Search these results</span>
              <input
                type="search"
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                placeholder="Search ID, queue, reason, error"
                data-shortcut="filter"
                className="w-80 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5"
              />
            </label>
            <span className="ml-auto flex items-center gap-3 text-xs text-[var(--color-text-muted)]">
              {showing === 'active' && total > 0 && (
                <button
                  type="button"
                  disabled={!mayReplay.allowed}
                  title={mayReplay.allowed ? (replayAllBusy ? 'A replay-all run is in progress — open it' : 'Replay every stuck message in this scope, one by one, in the background') : (mayReplay.reason ?? 'You cannot replay these messages')}
                  onClick={() => void openReplayAll({ provider, namespaceId: scope.ns?.id, environment: scope.env ?? undefined }, scopeLabel)}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--color-primary-700)] disabled:cursor-not-allowed disabled:opacity-50"
                >
                  <ListRestart className="h-4 w-4" aria-hidden="true" /> {replayAllBusy ? 'Replay all (running…)' : 'Replay All Messages'}
                </button>
              )}
              <button type="button" onClick={() => void refetch()} disabled={isFetching} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-sm font-semibold text-[var(--color-text)] hover:bg-[var(--color-surface-muted)] disabled:opacity-60">
                <RefreshCw className={`h-4 w-4 ${isFetching ? 'animate-spin' : ''}`} aria-hidden="true" /> Refresh
              </button>
              <InfoTip help={sectionHelp.dlq.refresh} />
              <span>Last updated<br />{dataUpdatedAt ? formatAgo(new Date(dataUpdatedAt).toISOString(), new Date()) : '—'}</span>
            </span>
          </div>

          <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            {rows.length === 0 ? (
              <EmptyState cloud={cloud} filtering={filtering} watched={watched} onClear={() => setParams(new URLSearchParams({ tab: 'dlq' }), { replace: true })} />
            ) : (
              <>
                {showing === 'active' && bulkBar('top')}
                <MessageTable
                  rows={rows}
                  namespaceNames={names}
                  showOutcome={showing !== 'active'}
                  caption={showing === 'active' ? undefined : 'Dead-lettered messages and what became of them, newest first'}
                  selection={showing !== 'active' ? undefined : { selected: current.all ? new Set(rows.map((r) => String(r.id))) : current.ids, onToggle: toggle, onTogglePage: togglePage }}
                />
                {showing === 'active' && bulkBar('bottom')}
                <Pager page={data.paging.page} pageSize={data.paging.pageSize} total={total} filtered={filtering} onPage={(p) => change({ page: String(p) })} onPageSize={(s) => { setPageSize(s); change({ page: null }) }} />
              </>
            )}
          </div>
          {reasonParam && groupTotal > 0 && (
            <p className="mt-2 text-xs text-[var(--color-text-muted)]">
              Showing {total.toLocaleString()} of {groupTotal.toLocaleString()} —{' '}
              <button type="button" onClick={() => change({ reason: null })} className="text-[var(--color-primary-700)] hover:underline">show all reasons</button>
            </p>
          )}
        </>
      )}
    </section>
  )
}

function EmptyState({ cloud, filtering, watched, onClear }: { cloud: string; filtering: boolean; watched: boolean; onClear: () => void }) {
  if (!filtering && !watched) {
    return (
      <p className="px-6 py-10 text-center text-sm text-[var(--color-text-muted)]">
        Nothing has been recorded for {cloud} yet. That does not mean there are no dead letters — use <b>Look now</b> above to ask {cloud}.
      </p>
    )
  }

  return filtering ? (
    <div className="px-6 py-10 text-center text-sm">
      <p className="font-medium">Nothing matches these filters.</p>
      <button type="button" onClick={onClear} className="mt-2 text-[var(--color-primary-700)] hover:underline">Clear the filters</button>
    </div>
  ) : (
    <div className="flex items-center justify-center gap-2 px-6 py-10 text-sm">
      <CheckCircle2 className="h-4 w-4 text-[var(--color-success)]" aria-hidden="true" />
      <span>
        No dead letters in {cloud} that ServiceHub has seen. New ones appear here as its scan finds them.
        {' '}
        <Link to="/" className="text-[var(--color-primary-700)] hover:underline">Back to Home</Link>
      </span>
    </div>
  )
}
