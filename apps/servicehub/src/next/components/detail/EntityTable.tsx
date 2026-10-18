import { CornerDownRight } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Pager } from '../../../components/ui/Pager'
import { usePagedList } from '../../../hooks/usePagedList'
import type { ConnectedComponent, Count } from '../../../lib/api/components'
import { formatWhen } from '../../../lib/format'
import { entityRows, fullName } from '../../lib/entityRows'
import { countWords, recordedWords } from '../../lib/words'
import { Th, ThHidden } from '../Tip'
import { Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'

/**
 * The places inside one connection (build-plan unit D2). Which columns appear depends on what the data holds, never on the cloud: a
 * dead-letter column only when some place has a dead-letter queue or topic of its own, "Active" only when the cloud counts, "Recorded"
 * only when ServiceHub does not watch. The entity's own limits (max deliveries, receives, attempts) are not returned by the entities
 * read, so they are not drawn; unit D6 shows them where an adapter supplies them.
 */
export function EntityTable({ component, active, detail }: {
  component: ConnectedComponent
  active: (fullPath: string) => Count | null
  detail: (entity: string) => string
}) {
  const rows = entityRows(component.entities, active)
  const { items, pager } = usePagedList(rows)
  const showStore = component.entities.some((e) => e.deadLetterStore !== null)
  const showActive = component.entities.some((e) => active(fullName(e)) !== null)
  const showRecorded = !component.watched
  const now = new Date()
  const columns = 5 + (showStore ? 1 : 0) + (showActive ? 1 : 0) + (showRecorded ? 1 : 0)

  return (
    <section aria-labelledby="places-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <h2 id="places-title" className="px-4 pt-4 text-base font-semibold">Queues, topics and subscriptions<Tip label="section:Queues, topics and subscriptions" /></h2>
      {component.entities.length === 0 ? (
        <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">
          {component.reachable ? 'No queue or subscription is listed for this connection.' : 'Its places cannot be listed until it can be reached.'}
        </p>
      ) : (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full border-t border-[var(--color-border)]">
            <caption className="sr-only">Queues, topics and subscriptions of {component.name}</caption>
            <thead>
              <tr>
                <Th>Entity</Th>
                <Th>Type</Th>
                {showStore && <Th>Dead-letter place</Th>}
                {showActive && <Th>Active</Th>}
                <Th>In dead-letter now</Th>
                {showRecorded && <Th>Recorded</Th>}
                <Th>Needs you</Th>
                <Th label={showRecorded ? 'Last look' : 'Last observed'}>{showRecorded ? 'Last look' : 'Last observed'}</Th>
                <ThHidden><span className="sr-only">Actions</span></ThHidden>
              </tr>
            </thead>
            <tbody>
              {items.map((r) =>
                r.kind === 'topic' ? (
                  <tr key={r.key} className="border-t border-[var(--color-border)] bg-[var(--color-surface-muted)]">
                    <th scope="row" className={`${td} text-left font-semibold`}>{r.name}</th>
                    <td className={td}>Topic</td>
                    <td colSpan={columns - 2} className={`${td} text-[var(--color-text-muted)]`}>A topic holds nothing itself. Its subscriptions do.</td>
                  </tr>
                ) : (
                  <tr key={r.key} className="border-t border-[var(--color-border)]">
                    <th scope="row" className={`${td} text-left font-semibold`}>
                      {r.entity.parent && <CornerDownRight className="mr-1 inline h-3.5 w-3.5 text-[var(--color-text-muted)]" aria-hidden="true" />}
                      {r.entity.name}
                    </th>
                    <td className={`${td} capitalize`}>{r.entity.kind}</td>
                    {showStore && <td className={td}>{r.entity.deadLetterStore ?? <span className="text-[var(--color-text-muted)]">None set</span>}</td>}
                    {showActive && <td className={td}>{r.active ? countWords(r.active) : '—'}</td>}
                    <td className={td}>{r.entity.noStoreReason ?? (r.entity.deadLetterStore === null && showStore ? '—' : countWords(r.entity.deadLetters))}</td>
                    {showRecorded && <td className={td}>{recordedWords(r.entity.recorded)}</td>}
                    <td className={`${td} font-semibold ${r.entity.needsYou > 0 ? 'text-red-700' : ''}`}>{r.entity.needsYou}</td>
                    <td className={td}>{r.entity.observedAtUtc ? formatWhen(r.entity.observedAtUtc, now) : '—'}</td>
                    <td className={td}><Link to={detail(fullName(r.entity))} aria-label={`View ${r.entity.name}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">View</Link></td>
                  </tr>
                ),
              )}
            </tbody>
          </table>
        </div>
      )}
      {showRecorded && component.entities.length > 0 && <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">“Recorded” is what ServiceHub kept at the last look.</p>}
      <Pager {...pager} label="queues, topics and subscriptions" />
    </section>
  )
}
