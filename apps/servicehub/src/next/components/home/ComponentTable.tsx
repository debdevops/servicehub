import { ChevronDown, ChevronRight, EllipsisVertical, Plus } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import type { ConnectedComponent } from '../../../lib/api/components'
import type { CloudKind } from '../../../lib/api/coverage'
import { formatWhen } from '../../../lib/format'
import { nextEntry, nextHref } from '../../nav/navigation'
import { Pager } from '../../../components/ui/Pager'
import { useComponentSource } from '../../../hooks/useComponentSource'
import { useSetComponent } from '../../../hooks/useComponentEnablement'
import { usePagedList } from '../../../hooks/usePagedList'
import { countWords, recordedWords } from '../../lib/words'
import { Th, ThHidden } from '../Tip'
import { Tip } from '../Tip'

// Narrow padding and names that may break anywhere: ten columns have to fit a 1366 px window without a side-scroll.
const th = 'px-2 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-2 py-2.5 align-top text-sm text-[var(--color-text)]'
const wrap = '[overflow-wrap:anywhere]'
const chip = 'inline-block whitespace-nowrap rounded-md border border-[var(--color-border)] px-2 py-0.5 text-xs font-medium text-[var(--color-text)]'

type Entry = { readonly kind: 'store'; readonly c: ConnectedComponent } | { readonly kind: 'demand'; readonly o: OnDemandRow }
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
            <Th className={th} label="Queues and subscriptions">Queues and subscriptions ({c.entities.length})</Th>
            <Th className={th}>Type</Th>
            <Th className={th}>In dead-letter now</Th>
            {showRecorded && <Th className={th}>Recorded</Th>}
            <Th className={th}>Needs you</Th>
            <Th className={th} label={showRecorded ? 'Last look' : 'Last observed'}>{showRecorded ? 'Last look' : 'Last observed'}</Th>
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

/** An added run-history component: read on request, so it has no counts of its own to show here. */
export interface OnDemandRow {
  readonly cloud: CloudKind
  readonly namespaceId: string
  readonly componentKey: string
  readonly componentName: string
  readonly connectionName: string
}

/**
 * One top row per connected component, expandable to its entities (build-plan unit C1). "Recorded" is a column only when some
 * component is not watched. Status is calculated; "Can’t reach" is also said in the Connection column, so it is never hidden
 * behind "Needs you".
 */
export function ComponentTable({ components, cloudName, canConnect = true, lookNow, onDemand = [] }: {
  components: readonly ConnectedComponent[]
  onDemand?: readonly OnDemandRow[]
  cloudName: string
  canConnect?: boolean
  lookNow?: React.ReactNode
}) {
  const { pathname } = useLocation()
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set())
  const showRecorded = components.some((c) => !c.watched)
  const toggle = (id: string) => setOpen((s) => { const next = new Set(s); if (!next.delete(id)) next.add(id); return next })
  const now = new Date()
  // Both kinds of row are paged as one list, so the count in the heading and the pager's "1–N of N" cannot disagree.
  const { items: pageRows, pager } = usePagedList<Entry>([...components.map((c): Entry => ({ kind: 'store', c })), ...onDemand.map((o): Entry => ({ kind: 'demand', o }))])
  const detail = (c: ConnectedComponent, entity?: string) =>
    `${nextHref(nextEntry('component-detail'), { cloud: c.cloud, component: c.componentKey, namespaceId: c.namespaceId })}${entity ? `?entity=${encodeURIComponent(entity)}` : ''}`

  return (
    <section aria-labelledby="components-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
        <h2 id="components-title" className="text-base font-semibold">{cloudName} PaaS components ({components.length + onDemand.length})<Tip label="section:Components" /></h2>
        <div className="flex items-center gap-2">
          {lookNow}
          <Link to={nextHref(nextEntry('add-component'), {}, pathname)} aria-disabled={!canConnect} className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-bold text-white hover:bg-[var(--color-primary-700)]">
            <Plus className="h-4 w-4" aria-hidden="true" /> Add a component
          </Link>
          <Tip label="Add a component" />
        </div>
      </div>

      {components.length + onDemand.length === 0 ? (
        <p className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">
          No {cloudName} component is connected yet. Use <Link className="font-semibold underline" to={nextHref(nextEntry('connect'), {}, pathname)}>Connect a cloud</Link> first, then <strong>Add a component</strong>.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-t border-[var(--color-border)]">
            <thead>
              <tr>
                <ThHidden className={`${th} w-8`}><span className="sr-only">Expand</span></ThHidden>
                <Th className={th}>Component</Th>
                <Th className={th}>Connection name</Th>
                <Th className={th}>Status</Th>
                <Th className={th}>Permissions</Th>
                <Th className={th}>In dead-letter now</Th>
                {showRecorded && <Th className={th}>Recorded</Th>}
                <Th className={th}>Needs you</Th>
                <Th className={th} label={showRecorded ? 'Last look' : 'Last observed'}>{showRecorded ? 'Last look' : 'Last observed'}</Th>
                <Th className={th}>Actions</Th>
              </tr>
            </thead>
            {pageRows.map((entry) => {
              if (entry.kind === 'demand') return <OnDemandTbody key={`${entry.o.namespaceId}:${entry.o.componentKey}`} c={entry.o} showRecorded={showRecorded} />
              const c = entry.c
              const id = key(c)
              const expanded = open.has(id)
              return (
                <tbody key={id} className="border-t border-[var(--color-border)]">
                  <tr>
                    <td className={td}>
                      <button type="button" aria-expanded={expanded} aria-label={`${expanded ? 'Hide' : 'Show'} the places in ${c.name} ${c.componentName}`} onClick={() => toggle(id)} className="rounded p-0.5 hover:bg-[var(--color-surface-muted)]">
                        {expanded ? <ChevronDown className="h-4 w-4" aria-hidden="true" /> : <ChevronRight className="h-4 w-4" aria-hidden="true" />}
                      </button>
                    </td>
                    <td className={`${td} font-semibold`}>{c.componentName}</td>
                    <td className={`${td} ${wrap}`}>{c.name}</td>
                    <td className={td}>
                      {c.reachable
                        ? <span className="inline-flex items-center gap-1.5 font-semibold text-green-700"><span aria-hidden="true" className="h-2 w-2 rounded-full bg-green-600" />Connected</span>
                        : <><span className="font-semibold text-amber-800">Can’t reach</span>{c.lastError && <span className="block max-w-[16rem] text-xs text-[var(--color-text-muted)]">{c.lastError}</span>}</>}
                    </td>
                    <td className={td}><span className={chip}>{c.canSend ? 'Read + Send' : 'Read only'}</span></td>
                    <td className={td}>{countWords(c.deadLetters)}</td>
                    {showRecorded && <td className={td}>{recordedWords(c.recorded)}</td>}
                    <td className={`${td} font-semibold ${c.needsYou > 0 ? 'text-red-700' : ''}`}>{c.needsYou}</td>
                    <td className={td}>{c.observedAtUtc ? formatWhen(c.observedAtUtc, now) : '—'}</td>
                    <td className={td}>
                      <div className="flex items-center gap-1">
                        <Link to={detail(c)} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]" aria-label={`View ${c.name} ${c.componentName}`}>View</Link>
                        <RowMenu label={`${c.name} ${c.componentName}`}><DeallocateButton namespaceId={c.namespaceId} componentKey={c.componentKey} label={`${c.name} ${c.componentName}`} /></RowMenu>
                      </div>
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

/** The ⋮ beside View: the rarely used actions of one row, out of the way (the design's three dots). */
function RowMenu({ label, children }: { label: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(false)
  return (
    <span className="relative">
      <button type="button" aria-haspopup="menu" aria-expanded={open} aria-label={`More actions for ${label}`} onClick={() => setOpen((v) => !v)} onKeyDown={(e) => { if (e.key === 'Escape') setOpen(false) }}
        className="rounded-lg p-1.5 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"><EllipsisVertical className="h-4 w-4" aria-hidden="true" /></button>
      {open && <span role="menu" className="absolute right-0 top-full z-30 mt-1 flex w-48 flex-wrap items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] p-2 shadow-lg">{children}</span>}
    </span>
  )
}

/** The places of an expanded on-demand component (for Storage Queues, its poison queues). Read only when the row is opened. */
function OnDemandPlaces({ c, detail }: { c: OnDemandRow; detail: (place?: string) => string }) {
  const source = useComponentSource(c.namespaceId, c.componentKey)
  const { items, pager } = usePagedList(source.data?.places ?? [])
  const hasDetail = (source.data?.places ?? []).some((p) => p.detail)
  if (source.isPending) return <p role="status" className="px-3 py-3 text-sm text-[var(--color-text-muted)]">Loading…</p>
  if (source.isError) return <p role="alert" className="px-3 py-3 text-sm text-red-800">Its places could not be read.</p>
  if (!source.data.available) return <p className="px-3 py-3 text-sm text-amber-900">{source.data.reason}</p>
  if (source.data.places.length === 0) return <p className="px-3 py-3 text-sm text-[var(--color-text-muted)]">Nothing is listed for this component.</p>
  return (
    <>
      <table className="w-full" aria-label={`Places in ${c.connectionName} ${c.componentName}`}>
        <thead>
          <tr>
            <Th className={th}>Places ({source.data.places.length})</Th>
            <Th className={th}>Type</Th>
            {hasDetail && <Th className={th}>Details</Th>}
            <ThHidden><span className="sr-only">View</span></ThHidden>
          </tr>
        </thead>
        <tbody>
          {items.map((p) => (
            <tr key={p.id} className="border-t border-[var(--color-border)]">
              <td className={`${td} ${wrap}`}>{p.name}</td>
              <td className={`${td} capitalize`}>{p.kind}</td>
              {hasDetail && <td className={td}>{p.detail ?? '—'}</td>}
              <td className={td}><Link to={detail(p.id)} className="text-xs font-semibold text-[var(--color-primary-700)]" aria-label={`View ${p.name}`}>View</Link></td>
            </tr>
          ))}
        </tbody>
      </table>
      <Pager {...pager} label={`places in ${c.connectionName} ${c.componentName}`} />
    </>
  )
}

/**
 * Deallocate: hide a component from ServiceHub (here, in the tiles and in the menu). It only saves a choice: nothing is read, changed or removed in the
 * cloud, the connection stays and the history is kept. Add a component brings it back. Two steps, so one stray click does nothing.
 */
function DeallocateButton({ namespaceId, componentKey, label }: { namespaceId: string; componentKey: string; label: string }) {
  const [confirming, setConfirming] = useState(false)
  const deallocate = useSetComponent(namespaceId)
  return (
    <>
      {confirming ? (
        <>
          <button type="button" disabled={deallocate.isPending} onClick={() => deallocate.mutate({ componentKey, enabled: false }, { onSettled: () => setConfirming(false) })} className="rounded-lg bg-red-700 px-3 py-1.5 text-xs font-semibold text-white hover:bg-red-800 disabled:opacity-60" aria-label={`Confirm deallocate ${label}`}>{deallocate.isPending ? 'Removing…' : 'Confirm'}</button>
          <button type="button" disabled={deallocate.isPending} onClick={() => setConfirming(false)} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold hover:bg-[var(--color-surface-muted)]">Cancel</button>
        </>
      ) : (
        <button type="button" onClick={() => setConfirming(true)} title="Hide from ServiceHub. Nothing changes in your cloud; add it again any time." className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-red-700 hover:bg-red-50" aria-label={`Deallocate ${label}`}>Deallocate</button>
      )}
      {deallocate.isError && <p role="alert" className="basis-full text-xs text-red-800">Could not deallocate. Try again.</p>}
    </>
  )
}

/** An added run-history component: read on request, so it has no counts here. Open it to see its places; View goes straight to its page. */
function OnDemandTbody({ c, showRecorded }: { c: OnDemandRow; showRecorded: boolean }) {
  const [expanded, setExpanded] = useState(false)
  const detail = (place?: string) =>
    `${nextHref(nextEntry('component-detail'), { cloud: c.cloud, component: c.componentKey, namespaceId: c.namespaceId })}${place ? `?place=${encodeURIComponent(place)}` : ''}`
  return (
    <tbody className="border-t border-[var(--color-border)]">
      <tr>
        <td className={td}>
          <button type="button" aria-expanded={expanded} aria-label={`${expanded ? 'Hide' : 'Show'} the places in ${c.connectionName} ${c.componentName}`} onClick={() => setExpanded((v) => !v)} className="rounded p-0.5 hover:bg-[var(--color-surface-muted)]">
            {expanded ? <ChevronDown className="h-4 w-4" aria-hidden="true" /> : <ChevronRight className="h-4 w-4" aria-hidden="true" />}
          </button>
        </td>
        <td className={`${td} font-semibold`}>{c.componentName}</td>
        <td className={td}>{c.connectionName}</td>
        <td className={td}><span className="inline-flex items-center gap-1 rounded-md bg-sky-50 px-2 py-0.5 text-xs font-semibold text-sky-800"><Plus className="h-3 w-3" aria-hidden="true" />Added</span></td>
        <td className={td}><span className={chip}>Read only</span></td>
        <td className={td}>—</td>
        {showRecorded && <td className={td}>—</td>}
        <td className={td}>—</td>
        <td className={td}>—</td>
        <td className={td}>
          <div className="flex items-center gap-1">
            <Link to={detail()} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]" aria-label={`View ${c.connectionName} ${c.componentName}`}>View</Link>
            <RowMenu label={`${c.connectionName} ${c.componentName}`}><DeallocateButton namespaceId={c.namespaceId} componentKey={c.componentKey} label={`${c.connectionName} ${c.componentName}`} /></RowMenu>
          </div>
        </td>
      </tr>
      {expanded && (
        <tr>
          <td />
          <td colSpan={showRecorded ? 9 : 8} className="bg-[var(--color-surface-muted)] p-0"><OnDemandPlaces c={c} detail={detail} /></td>
        </tr>
      )}
    </tbody>
  )
}
