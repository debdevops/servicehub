import { ChevronDown, ChevronRight, Plus } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import type { ConnectedComponent } from '../../../lib/api/components'
import { formatWhen } from '../../../lib/format'
import { nextEntry, nextHref } from '../../nav/navigation'
import { Pager } from '../../../components/ui/Pager'
import { usePagedList } from '../../../hooks/usePagedList'
import { countWords, recordedWords, statusWords } from '../../lib/words'
import { Th, ThHidden } from '../Tip'
import { Tip } from '../Tip'

const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm text-[var(--color-text)]'
const tone = { bad: 'bg-red-50 text-red-700', warn: 'bg-amber-50 text-amber-800', ok: 'text-[var(--color-text-muted)]' } as const

const key = (c: ConnectedComponent) => `${c.namespaceId}:${c.componentKey}`

/** The places inside one component, paged on their own: a connection can hold hundreds of queues. */
function EntityTable({ component: c, showRecorded, now, detail }: {
  component: ConnectedComponent
  showRecorded: boolean
  now: Date
  detail: (c: ConnectedComponent, entity?: string) => string
}) {
  const { items, pager } = usePagedList(c.entities)
  return (
    <>
      <table className="w-full" aria-label={`Places in ${c.name} ${c.componentName}`}>
        <thead>
          <tr>
            <Th label="Queues and subscriptions">Queues and subscriptions ({c.entities.length})</Th>
            <Th>Type</Th>
            <Th>In dead-letter now</Th>
            {showRecorded && <Th>Recorded</Th>}
            <Th>Needs you</Th>
            <Th label={showRecorded ? 'Last look' : 'Last observed'}>{showRecorded ? 'Last look' : 'Last observed'}</Th>
            <ThHidden><span className="sr-only">View</span></ThHidden>
          </tr>
        </thead>
        <tbody>
          {items.map((e) => (
            <tr key={`${e.parent ?? ''}/${e.name}`} className="border-t border-[var(--color-border)]">
              <td className={td}>{e.parent ? `${e.parent} / ${e.name}` : e.name}{e.deadLetterStore && <span className="text-[var(--color-text-muted)]"> → {e.deadLetterStore}</span>}</td>
              <td className={`${td} capitalize`}>{e.kind}</td>
              <td className={td}>{e.noStoreReason ?? countWords(e.deadLetters)}</td>
              {showRecorded && <td className={td}>{recordedWords(e.recorded)}</td>}
              <td className={`${td} font-semibold`}>{e.needsYou}</td>
              <td className={td}>{e.observedAtUtc ? formatWhen(e.observedAtUtc, now) : '—'}</td>
              <td className={td}><Link to={detail(c, e.name)} className="text-xs font-semibold text-[var(--color-primary-700)]" aria-label={`View ${e.name}`}>View</Link></td>
            </tr>
          ))}
        </tbody>
      </table>
      <Pager {...pager} label={`places in ${c.name} ${c.componentName}`} />
    </>
  )
}

/**
 * One top row per connected component, expandable to its entities (build-plan unit C1). "Recorded" is a column only when some
 * component is not watched. Status is calculated; "Can’t reach" is also said in the Connection column, so it is never hidden
 * behind "Needs you".
 */
export function ComponentTable({ components, cloudName, canConnect = true, lookNow }: {
  components: readonly ConnectedComponent[]
  cloudName: string
  canConnect?: boolean
  lookNow?: React.ReactNode
}) {
  const { pathname } = useLocation()
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set())
  const showRecorded = components.some((c) => !c.watched)
  const toggle = (id: string) => setOpen((s) => { const next = new Set(s); if (!next.delete(id)) next.add(id); return next })
  const now = new Date()
  const { items: pageRows, pager } = usePagedList(components)
  const detail = (c: ConnectedComponent, entity?: string) =>
    `${nextHref(nextEntry('component-detail'), { cloud: c.cloud, component: c.componentKey, namespaceId: c.namespaceId })}${entity ? `?entity=${encodeURIComponent(entity)}` : ''}`

  return (
    <section aria-labelledby="components-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
        <h2 id="components-title" className="text-base font-semibold">{cloudName} PaaS components ({components.length})<Tip label="section:Components" /></h2>
        <div className="flex items-center gap-2">
          {lookNow}
          <Link to={nextHref(nextEntry('connect'), {}, pathname)} aria-disabled={!canConnect} className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-bold text-white hover:bg-[var(--color-primary-700)]">
            <Plus className="h-4 w-4" aria-hidden="true" /> Connect a component
          </Link>
          <Tip label="Connect a component" />
        </div>
      </div>

      {components.length === 0 ? (
        <p className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">
          No {cloudName} component is connected yet. Use <strong>Connect a component</strong> to add one.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-t border-[var(--color-border)]">
            <thead>
              <tr>
                <ThHidden className={`${th} w-8`}><span className="sr-only">Expand</span></ThHidden>
                <Th>Name</Th>
                <Th>Type</Th>
                <Th>Connection</Th>
                <Th>Status</Th>
                <Th>In dead-letter now</Th>
                {showRecorded && <Th>Recorded</Th>}
                <Th>Needs you</Th>
                <Th label={showRecorded ? 'Last look' : 'Last observed'}>{showRecorded ? 'Last look' : 'Last observed'}</Th>
                <Th>Actions</Th>
              </tr>
            </thead>
            {pageRows.map((c) => {
              const id = key(c)
              const expanded = open.has(id)
              const status = statusWords(c)
              return (
                <tbody key={id} className="border-t border-[var(--color-border)]">
                  <tr>
                    <td className={td}>
                      <button type="button" aria-expanded={expanded} aria-label={`${expanded ? 'Hide' : 'Show'} the places in ${c.name} ${c.componentName}`} onClick={() => toggle(id)} className="rounded p-0.5 hover:bg-[var(--color-surface-muted)]">
                        {expanded ? <ChevronDown className="h-4 w-4" aria-hidden="true" /> : <ChevronRight className="h-4 w-4" aria-hidden="true" />}
                      </button>
                    </td>
                    <td className={`${td} font-semibold`}>{c.name}</td>
                    <td className={td}>{c.componentName}</td>
                    <td className={td}>
                      {c.reachable
                        ? <><span className="font-semibold text-green-700">Connected</span><span className="block text-xs text-[var(--color-text-muted)]">{c.canSend ? 'Read + Send' : 'Read only'}</span></>
                        : <><span className="font-semibold text-amber-800">Can’t reach</span>{c.lastError && <span className="block max-w-[16rem] text-xs text-[var(--color-text-muted)]">{c.lastError}</span>}</>}
                    </td>
                    <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${tone[status.tone]}`}>{status.text}</span></td>
                    <td className={td}>{countWords(c.deadLetters)}</td>
                    {showRecorded && <td className={td}>{recordedWords(c.recorded)}</td>}
                    <td className={`${td} font-semibold ${c.needsYou > 0 ? 'text-red-700' : ''}`}>{c.needsYou}</td>
                    <td className={td}>{c.observedAtUtc ? formatWhen(c.observedAtUtc, now) : '—'}</td>
                    <td className={td}>
                      <Link to={detail(c)} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]" aria-label={`View ${c.name} ${c.componentName}`}>View</Link>
                    </td>
                  </tr>
                  {expanded && (
                    <tr>
                      <td />
                      <td colSpan={showRecorded ? 9 : 8} className="bg-[var(--color-surface-muted)] p-0">
                        {c.entities.length === 0 ? (
                          <p className="px-3 py-3 text-sm text-[var(--color-text-muted)]">{c.reachable ? 'No queue or subscription is listed for this component.' : 'Its places cannot be listed until it can be reached.'}</p>
                        ) : (
                          <EntityTable component={c} showRecorded={showRecorded} now={now} detail={detail} />
                        )}
                      </td>
                    </tr>
                  )}
                </tbody>
              )
            })}
          </table>
        </div>
      )}
      {showRecorded && <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">“Recorded” is what ServiceHub kept at the last look</p>}
      <Pager {...pager} label="components" />
    </section>
  )
}
