import { useEffect, useState } from 'react'
import { namespaceTag } from '../provider/scopeChoice'
import { usePageSize } from '../../lib/pageSize'
import { Link, useSearchParams } from 'react-router-dom'
import { CheckCircle2, TriangleAlert } from 'lucide-react'
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
import type { DeadLetterRange } from '../../lib/api/deadLetters'
import { bulkSelection } from '../../lib/bulkSelection'
import { environmentMeta, resolveScope } from '../provider/scopeChoice'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'


const ranges: readonly { id: DeadLetterRange; label: string }[] = [
  { id: 'all', label: 'All time' },
  { id: '24h', label: 'Last 24 hours' },
  { id: '7d', label: 'Last 7 days' },
  { id: '30d', label: 'Last 30 days' },
]

const asRange = (v: string | null): DeadLetterRange => (v === '24h' || v === '7d' || v === '30d' ? v : 'all')

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
  const reasonParam = params.get('reason')
  const entity = params.get('entity') ?? undefined
  const q = params.get('q') ?? ''

  const query = {
    provider,
    // One namespace in scope (`?ns=`) → ask the API for just that one; a whole environment (`?env=`) → for just that environment.
    namespaceId: scope.ns?.id,
    environment: scope.env ?? undefined,
    status: 'active' as const,
    range,
    reason: reasonParam && reasonParam !== NO_REASON ? reasonParam : undefined,
    noReason: reasonParam === NO_REASON,
    entity,
    q: q || undefined,
    page,
    pageSize,
  }
  const { data, isPending, isError, refetch } = useDeadLetters(query)

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
  const signature = [reasonParam, entity, range, q].join('|')
  const [selection, setSelection] = useState<{ sig: string; ids: ReadonlySet<string>; all: boolean }>({ sig: signature, ids: new Set(), all: false })
  const current = selection.sig === signature ? selection : { sig: signature, ids: new Set<string>(), all: false }

  // Whether ServiceHub looks in this cloud on its own. Where it does not (a peek there is a delivery
  // attempt), an empty list is silence, not good news — and must not read as good news (R5).
  const watched = namespaces.every((n) => n.capabilities?.supportsRepeatablePeek === true)
  const names = new Map(namespaces.map((n) => [n.id, namespaceTag(n)]))
  const total = data?.paging.total ?? 0
  const filtering = !!(reasonParam || entity || q || range !== 'all')
  const reasonLabel = reasonParam === NO_REASON ? 'with no reason recorded' : reasonParam
  const groupTotal = (data?.groups ?? []).reduce((n, g) => n + g.count, 0) + (data?.otherReasons?.count ?? 0)

  const rows = data?.items ?? []
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

  return (
    <section className="px-6 py-6">
      <header className="mb-4">
        <h1 className="flex items-center gap-2 text-2xl font-semibold text-[var(--color-text)]">
          {cloud} — Dead letters <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
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
      </header>

      {!watched && <LookNow cloud={cloud} namespaces={namespaces.filter((n) => n.capabilities?.supportsRepeatablePeek !== true)} />}

      {explainer.shown && <ExplainerCard id="dead-letters" onDismiss={explainer.dismiss} />}

      <WorkTabs current="dlq" />

      {isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading dead letters…</p>}

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
            <label className="flex items-center gap-2">
              <span className="text-[var(--color-text-muted)]">Window</span>
              <select value={range} onChange={(e) => change({ range: e.target.value === 'all' ? null : e.target.value })} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5">
                {ranges.map((r) => (
                  <option key={r.id} value={r.id}>{r.label}</option>
                ))}
              </select>
            </label>
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
                placeholder="Search by message ID, queue or reason"
                className="w-72 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5"
              />
            </label>
            <span className="ml-auto text-xs text-[var(--color-text-muted)]">Newest first</span>
          </div>

          <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            {rows.length === 0 ? (
              <EmptyState cloud={cloud} filtering={filtering} watched={watched} onClear={() => setParams(new URLSearchParams({ tab: 'dlq' }), { replace: true })} />
            ) : (
              <>
                {selectedCount > 0 && (
                  <BulkBar
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
                )}
                <MessageTable
                  rows={rows}
                  namespaceNames={names}
                  selection={{ selected: current.all ? new Set(rows.map((r) => String(r.id))) : current.ids, onToggle: toggle, onTogglePage: togglePage }}
                />
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
