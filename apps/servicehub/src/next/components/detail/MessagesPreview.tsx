import { Link, useLocation } from 'react-router-dom'
import { useDeadLetters } from '../../../hooks/useDeadLetters'
import { usePendingWork } from '../../../hooks/usePendingWork'
import type { Me } from '../../../lib/api/identity'
import { formatWhen } from '../../../lib/format'
import { permission } from '../../../lib/permissions'
import { entityLabel } from '../../lib/entityRows'

const PREVIEW = 5

const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'

/**
 * The newest few dead letters of this connection, with where each stands (build-plan unit D2). A preview, not the list: the full,
 * paged list is the Dead-letter messages tab (D3), one link away. A message waiting on a person says so and offers Review or View
 * from the same pending-work read the Home table uses, so the two never tell different stories.
 */
export function MessagesPreview({ namespaceId, watched, me, tabHref }: { namespaceId: string; watched: boolean; me: Me | undefined; tabHref: string }) {
  const { pathname } = useLocation()
  const list = useDeadLetters({ namespaceId, status: 'active', page: 1, pageSize: PREVIEW })
  const pending = usePendingWork({ namespaceId })
  const waiting = new Map((pending.data?.items ?? []).filter((i) => i.dlqMessageId !== null).map((i) => [i.dlqMessageId as number, i]))
  const total = list.data?.paging.total ?? 0
  const items = list.data?.items ?? []
  const now = new Date()
  const title = watched ? `Dead-letter messages (${total.toLocaleString('en-US')})` : `Recorded dead-letter messages (showing ${items.length} of ${total.toLocaleString('en-US')})`

  return (
    <section aria-labelledby="preview-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
        <h2 id="preview-title" className="text-base font-semibold">{title}</h2>
        <Link to={tabHref} className="text-xs font-semibold text-[var(--color-primary-700)]">Open the Dead-letter messages tab →</Link>
      </div>
      {list.isError ? (
        <p role="alert" className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-red-800">The dead-letter messages could not be read.</p>
      ) : list.isPending ? (
        <p role="status" className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : items.length === 0 ? (
        <p className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">{watched ? 'No message is in a dead-letter queue.' : 'Nothing is recorded yet. Look now records what is in the dead-letter queues.'}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-t border-[var(--color-border)]">
            <caption className="sr-only">The newest dead-letter messages of this connection</caption>
            <thead>
              <tr><th className={th}>Message</th><th className={th}>Entity</th><th className={th}>Reason</th><th className={th}>Deliveries</th><th className={th}>{watched ? 'Detected' : 'Recorded'}</th><th className={th}>State</th><th className={th}>Actions</th></tr>
            </thead>
            <tbody>
              {items.map((m) => {
                const held = waiting.get(m.id)
                const hand = held?.kind === 'unresolved'
                const may = permission(me, 'Approver', 'approve or decline a replay', { recover: true, namespaceId })
                const state = held ? (hand ? { text: 'Handed to a person', cls: 'bg-amber-50 text-amber-800' } : { text: 'Waiting for approval', cls: 'bg-red-50 text-red-700' }) : { text: watched ? 'In dead-letter' : 'Recorded', cls: 'bg-slate-100 text-slate-700' }
                const open = held ? `${pathname}?${hand ? 'panel=hand-off' : 'dialog=review'}&entry=${encodeURIComponent(held.entryId ?? held.id)}` : `${pathname}?dialog=message&id=${m.id}`
                return (
                  <tr key={m.id} className="border-t border-[var(--color-border)]">
                    <td className={`${td} font-mono text-xs`}>{m.messageId}</td>
                    <td className={td}>{entityLabel(m.entityName, m.topicName)}</td>
                    <td className={td}>{m.deadLetterReason ?? '—'}</td>
                    <td className={td}>{m.deliveryCount}</td>
                    <td className={td}>{formatWhen(m.detectedAtUtc, now)}</td>
                    <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${state.cls}`}>{state.text}</span></td>
                    <td className={td}>
                      {held && !hand && !may.allowed
                        ? <span title={may.reason ?? undefined} className="text-xs text-[var(--color-text-muted)]">Needs an Approver</span>
                        : <Link to={open} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">{held && !hand ? 'Review' : 'View'}</Link>}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
