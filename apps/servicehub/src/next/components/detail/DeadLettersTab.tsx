import { Search } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { BulkBar } from '../../../components/message/BulkBar'
import { FailureGroups, NO_REASON } from '../../../components/message/FailureGroups'
import { Pager } from '../../../components/ui/Pager'
import { useDeadLetters } from '../../../hooks/useDeadLetters'
import { usePendingWork } from '../../../hooks/usePendingWork'
import type { ConnectedComponent } from '../../../lib/api/components'
import type { Me } from '../../../lib/api/identity'
import { BULK_LIMIT, bulkSelection } from '../../../lib/bulkSelection'
import { formatWhen } from '../../../lib/format'
import { usePageSize } from '../../../lib/pageSize'
import { permission } from '../../../lib/permissions'
import { entityLabel } from '../../lib/entityRows'

type Showing = 'active' | 'resolved' | 'all'
const showings: readonly { id: Showing; label: string }[] = [{ id: 'active', label: 'Stuck now' }, { id: 'resolved', label: 'No longer stuck' }, { id: 'all', label: 'All' }]
const asShowing = (v: string | null): Showing => (v === 'resolved' || v === 'all' ? v : 'active')

const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'
const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm'

/**
 * The Dead-letter messages tab of one connection (build-plan unit D3; design `p05a-service-bus-dead-letters`). Every filter is in the
 * address, so a filtered view is a link that survives a refresh. A message opens the Message dialog, one waiting on a person opens Review
 * or the hand-off panel. **Nothing is replayed from this list**: ticking messages and choosing Replay selected opens Bulk replay (F3), whose
 * PREVIEW comes first; a message held for a person is not tickable (Review answers it).
 * Failure groups are by RECORDED reason, a fact the cloud or the application wrote down, not a category ServiceHub guessed.
 */
export function DeadLettersTab({ component: c, me }: { component: ConnectedComponent; me: Me | undefined }) {
  const { pathname } = useLocation()
  const [params, setParams] = useSearchParams()
  const [pageSize, setPageSize] = usePageSize()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const showing = asShowing(params.get('status'))
  const reason = params.get('reason')
  const entity = params.get('entity') ?? undefined
  const q = params.get('q') ?? ''

  const filter = { namespaceId: c.namespaceId, status: showing, reason: reason && reason !== NO_REASON ? reason : undefined, noReason: reason === NO_REASON, entity, q: q || undefined } as const
  const list = useDeadLetters({ ...filter, page, pageSize })
  const pending = usePendingWork({ namespaceId: c.namespaceId })
  const waiting = new Map((pending.data?.items ?? []).filter((i) => i.dlqMessageId !== null).map((i) => [i.dlqMessageId as number, i]))

  const change = (patch: Record<string, string | null>) =>
    setParams((current) => {
      const next = new URLSearchParams(current)
      for (const [k, v] of Object.entries(patch)) {
        if (v === null || v === '') next.delete(k)
        else next.set(k, v)
      }
      if (!('page' in patch)) next.delete('page') // a new filter starts at the top
      return next
    }, { replace: true })

  // Search is typed, so it is applied a moment after the last keystroke rather than on each one.
  const [draft, setDraft] = useState(q)
  useEffect(() => {
    if (draft === q) return
    const t = setTimeout(() => change({ q: draft.trim() || null }), 300)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft])

  const total = list.data?.paging.total ?? 0
  const items = list.data?.items ?? []

  // What is ticked, keyed by the filters it was ticked under: a new filter or connection starts with nothing ticked.
  const signature = JSON.stringify([c.namespaceId, showing, reason, entity, q])
  const [selection, setSelection] = useState<{ sig: string; ids: ReadonlySet<number>; all: boolean }>({ sig: signature, ids: new Set(), all: false })
  const current = selection.sig === signature ? selection : { sig: signature, ids: new Set<number>(), all: false }
  const tickable = items.filter((m) => m.status === 'active' && !waiting.has(m.id))
  const toggle = (id: number) => { const ids = new Set(current.ids); if (ids.has(id)) ids.delete(id); else ids.add(id); setSelection({ sig: signature, ids, all: false }) }
  const togglePage = () => {
    const every = tickable.length > 0 && tickable.every((m) => current.ids.has(m.id))
    const ids = new Set(current.ids)
    tickable.forEach((m) => (every ? ids.delete(m.id) : ids.add(m.id)))
    setSelection({ sig: signature, ids, all: false })
  }
  // A bulk run that has ended has used up its selection.
  useEffect(() => bulkSelection.onFinished(() => setSelection({ sig: signature, ids: new Set(), all: false })), [signature])
  const selectedCount = current.all ? Math.min(total, BULK_LIMIT) : current.ids.size
  const bulkBar = (edge: 'top' | 'bottom') => (
    <BulkBar edge={edge} hideDetails replayHref={`${pathname}?tab=dead-letters&dialog=bulk-replay`}
      allOnPage={tickable.length > 0 && tickable.every((m) => current.ids.has(m.id))} onTogglePage={togglePage}
      count={selectedCount} allMatching={current.all} matchingTotal={Math.min(total, BULK_LIMIT)}
      matchingLabel={reason === NO_REASON ? 'with no reason recorded' : reason ?? 'dead letters'}
      canSelectAll={!!reason && total > items.length && showing === 'active'}
      onSelectAll={() => setSelection({ sig: signature, ids: new Set(tickable.map((m) => m.id)), all: true })}
      onClear={() => setSelection({ sig: signature, ids: new Set(), all: false })}
      onOpen={() => bulkSelection.set(current.all ? { query: { ...filter, page: 1, pageSize: 100 }, total } : { ids: [...current.ids] })} />
  )
  const filtering = !!(reason || entity || q || showing !== 'active')
  const now = new Date()
  const title = c.watched ? `Dead-letter messages (${total.toLocaleString('en-US')})` : `Recorded dead-letter messages (showing ${items.length.toLocaleString('en-US')} of ${total.toLocaleString('en-US')})`

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm">Entity
          <select aria-label="Entity" className={field} value={entity ?? ''} onChange={(e) => change({ entity: e.target.value || null })}>
            <option value="">All</option>
            {(list.data?.entities ?? []).map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <label className="flex items-center gap-2 text-sm">State
          <select aria-label="State" className={field} value={showing} onChange={(e) => change({ status: e.target.value === 'active' ? null : e.target.value })}>
            {showings.map((s) => <option key={s.id} value={s.id}>{s.label}</option>)}
          </select>
        </label>
        <label className="relative flex items-center">
          <Search className="pointer-events-none absolute left-2.5 h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
          <input type="search" aria-label="Search messages" placeholder="Message ID or reason" value={draft} onChange={(e) => setDraft(e.target.value)} className={`${field} pl-8`} />
        </label>
        {filtering && <button type="button" onClick={() => { setDraft(''); change({ entity: null, status: null, reason: null, q: null }) }} className="text-sm font-semibold text-[var(--color-primary-700)]">Clear filters</button>}
      </div>

      {list.data && <FailureGroups groups={list.data.groups} other={list.data.otherReasons} selected={reason} onSelect={(r) => change({ reason: r })} />}

      <section aria-labelledby="dl-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <h2 id="dl-title" className="px-4 pt-4 text-base font-semibold">{title}</h2>
        {list.isError ? (
          <p role="alert" className="px-4 py-6 text-sm text-red-800">The dead-letter messages could not be read.</p>
        ) : list.isPending ? (
          <p role="status" className="px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
        ) : items.length === 0 ? (
          <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">
            {filtering ? 'Nothing matches these filters.' : c.watched ? 'No message is in a dead-letter queue.' : 'Nothing is recorded yet. Look now records what is in the dead-letter queues.'}
          </p>
        ) : (
          <div className="mt-3 overflow-x-auto">
            {c.watched || items.length > 0 ? bulkBar('top') : null}
            <table className="w-full border-t border-[var(--color-border)]">
              <caption className="sr-only">Dead-letter messages of {c.name}</caption>
              <thead>
                <tr><th className={th}><span className="sr-only">Select</span></th><th className={th}>Message</th><th className={th}>Entity</th><th className={th}>Reason</th><th className={th}>Description</th><th className={th}>Deliveries</th><th className={th}>Enqueued</th><th className={th}>{c.watched ? 'Detected' : 'Recorded'}</th><th className={th}>State</th><th className={th}>Actions</th></tr>
              </thead>
              <tbody>
                {items.map((m) => {
                  const held = waiting.get(m.id)
                  const hand = held?.kind === 'unresolved'
                  const may = permission(me, 'Approver', 'approve or decline a replay', { recover: true, namespaceId: c.namespaceId })
                  const state = held ? (hand ? { text: 'Handed to a person', cls: 'bg-amber-50 text-amber-800' } : { text: 'Waiting for approval', cls: 'bg-red-50 text-red-700' })
                    : m.status === 'active' ? { text: c.watched ? 'In dead-letter' : 'Recorded', cls: 'bg-slate-100 text-slate-700' } : { text: 'No longer stuck', cls: 'bg-green-50 text-green-800' }
                  const open = held ? `${pathname}?tab=dead-letters&${hand ? 'panel=hand-off' : 'dialog=review'}&entry=${encodeURIComponent(held.entryId ?? held.id)}` : `${pathname}?tab=dead-letters&dialog=message&id=${m.id}`
                  return (
                    <tr key={m.id} className="border-t border-[var(--color-border)]">
                      <td className={td}>
                        {m.status === 'active' && !held
                          ? <input type="checkbox" className="h-4 w-4" checked={current.all || current.ids.has(m.id)} disabled={current.all} onChange={() => toggle(m.id)} aria-label={`Select message ${m.messageId}`} />
                          : null}
                      </td>
                      <td className={`${td} font-mono text-xs`}>{m.messageId}</td>
                      <td className={td}>{entityLabel(m.entityName, m.topicName)}</td>
                      <td className={td}>{m.deadLetterReason ?? '—'}</td>
                      <td className={td}>{m.deadLetterErrorDescription ?? '—'}</td>
                      <td className={td}>{m.deliveryCount}</td>
                      <td className={td}>{formatWhen(m.enqueuedTimeUtc, now)}</td>
                      <td className={td}>{formatWhen(m.detectedAtUtc, now)}</td>
                      <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${state.cls}`}>{state.text}</span></td>
                      <td className={td}>
                        <span className="flex flex-wrap items-center gap-2">
                          <Link to={`${pathname}?tab=dead-letters&dialog=message&id=${m.id}`} aria-label={`Open message ${m.messageId}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">Open</Link>
                          {held && (hand || may.allowed
                            ? <Link to={open} className="rounded-lg bg-[var(--color-primary-600)] px-3 py-1.5 text-xs font-bold text-white">{hand ? 'View' : 'Review'}</Link>
                            : <span title={may.reason ?? undefined} className="text-xs text-[var(--color-text-muted)]">Needs an Approver</span>)}
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
            {bulkBar('bottom')}
          </div>
        )}
        <Pager page={page} pageSize={pageSize} total={total} filtered={filtering} onPage={(p) => change({ page: String(p) })} onPageSize={(n) => { setPageSize(n); change({}) }} label="dead-letter messages" />
      </section>

      <p role="note" className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-2.5 text-sm">
        {c.watched
          ? <><strong>{c.deadLetters.kind === 'exact' ? 'Counts are exact here.' : 'This list is what ServiceHub has seen.'}</strong> This connection is watched automatically, so the list is every dead-letter message ServiceHub has seen, not a sample.</>
          : <><strong>This is what ServiceHub recorded when you last looked.</strong> It is not watched in the background, so messages that failed since are not here until you press Look now.</>}
        {' '}Nothing is replayed from this list; choosing Replay selected shows a preview first.
      </p>
    </div>
  )
}
