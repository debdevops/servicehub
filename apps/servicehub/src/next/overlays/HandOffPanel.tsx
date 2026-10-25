import { CircleAlert } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { useMe } from '../../hooks/useIdentity'
import { usePendingWork, useResolvePending } from '../../hooks/usePendingWork'
import { useLedgerEntry } from '../../hooks/useRecoverySummary'
import { toProblem } from '../../lib/api/client'
import { formatAge, formatWhen } from '../../lib/format'
import { describeEvent } from '../../lib/ledgerWords'
import { permission } from '../../lib/permissions'
import { cloudLabel } from '../lib/clouds'
import { entityLabel } from '../lib/entityRows'
import { familyWords } from '../lib/words'
import type { NextOverlayBodyProps } from './registry'
import { Tip } from '../components/Tip'

function Row({ label, children }: { label: string; children: ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 text-sm"><dt className="w-32 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 break-words font-semibold">{children}</dd></div>
}

/**
 * Handed to a person (build-plan unit F4; design `d06-hand-off`), at `?panel=hand-off&entry=<id>`: what ServiceHub stopped on, where,
 * since when, why it stops, and what a person can do. It acts on nothing by itself. The three choices are real: **Replay it again**
 * opens the Replay dialog (its own preview and safety checks), **Mark as settled** records what the person found, with their name, and
 * closes the attempt as written off — not as recovered or failed, because nobody has proved either (`POST /pending-work/{id}/resolve`) —
 * and **Leave it** closes the panel and changes nothing. What is not drawn is what no endpoint carries: which channel was told and when
 * (a channel has no component in 5.0.0, so it says every enabled channel is told), and a cloud-console link.
 */
export default function HandOffPanel({ close }: NextOverlayBodyProps) {
  const [params] = useSearchParams()
  const { pathname } = useLocation()
  const entryId = params.get('entry')
  const pending = usePendingWork()
  const entry = useLedgerEntry(entryId)
  const me = useMe()
  const resolve = useResolvePending()
  const [settling, setSettling] = useState(false)
  const [found, setFound] = useState('')
  useOverlayTitle('Handed to a person', 'ServiceHub stopped on this and will not act on it by itself.')

  if (pending.isError) return <p role="alert" className="text-sm">What was handed to a person could not be read.</p>
  if (!pending.data) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
  const item = pending.data.items.find((i) => i.kind === 'unresolved' && i.entryId === entryId)

  if (resolve.isSuccess) {
    return (
      <div className="space-y-3" role="status">
        <p className="text-base font-semibold">Recorded with your name.</p>
        <p className="text-sm text-[var(--color-text-muted)]">The attempt is closed without a verdict. If you want the message put back, replay it: that is a new, checked attempt.</p>
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold">Done</button>
      </div>
    )
  }
  if (!item) {
    return (
      <div className="space-y-3">
        <p className="text-sm">Nothing is handed to a person under that link. It may already have been settled.</p>
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold">Close</button>
      </div>
    )
  }

  const now = new Date()
  const where = [item.provider ? cloudLabel[item.provider] : null, item.namespaceName, item.entity && entityLabel(item.entity, null)].filter(Boolean).join(' · ')
  const may = permission(me.data, 'Approver', 'say what happened to an attempt', { recover: true, namespaceId: item.namespaceId ?? undefined })
  const mayReplay = permission(me.data, 'Operator', 'replay this message', { recover: true, namespaceId: item.namespaceId ?? undefined })
  const cloud = item.provider ? cloudLabel[item.provider] : 'the cloud'
  const events = entry.data?.events ?? []

  return (
    <div className="space-y-4">
      <p className="flex items-start gap-2 rounded-xl border border-red-300 bg-red-50 px-3.5 py-2.5 text-sm text-red-900">
        <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <span><b>ServiceHub stopped here. A person decides.</b> Nothing will retry this message by itself.</span>
      </p>

      <dl>
        <Row label="What">{item.dlqMessageId !== null ? `Message #${item.dlqMessageId}` : 'A message'}{item.deadLetterReason ? ` failed with ${item.deadLetterReason}` : ''}</Row>
        <Row label="Where">{where || '—'}</Row>
        <Row label="Since">{formatWhen(item.since, now)} ({formatAge(item.since, now)} ago)</Row>
        <Row label="Why it stops">{item.reason}</Row>
        {familyWords(item.family) && <Row label="Family"><span className="rounded-md bg-purple-50 px-2 py-0.5 text-xs font-semibold text-purple-800">{familyWords(item.family)}</span></Row>}
        <Row label="Who was told">Every enabled notification channel, and the bell (see Configuration)</Row>
        <Row label="Where to look next">The consumer of {item.entity ? entityLabel(item.entity, null) : 'this queue'}, then the queue itself in {cloud}’s own console</Row>
      </dl>

      <section aria-label="What you can do">
        <h3 className="mb-2 text-sm font-bold">What you can do<Tip label="section:What you can do" /></h3>
        <div className="grid gap-3 @lg:grid-cols-3">
          <div className="rounded-xl border border-[var(--color-border)] p-3 text-sm">
            <p className="flex items-center font-bold">Replay it again<Tip label="choice:Replay it again" /></p>
            <p className="mt-1 text-xs text-[var(--color-text-muted)]">Opens the Replay preview. Only useful if the cause was fixed.</p>
            {item.dlqMessageId !== null && mayReplay.allowed
              ? <Link to={`${pathname}?dialog=replay&id=${item.dlqMessageId}`} className="mt-2 inline-block rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)]">Replay…</Link>
              : <p className="mt-2 text-xs text-[var(--color-text-muted)]">{mayReplay.reason ?? 'There is no message to replay.'}</p>}
          </div>
          <div className="rounded-xl border border-[var(--color-border)] p-3 text-sm">
            <p className="flex items-center font-bold">Mark as settled<Tip label="choice:Mark as settled" /></p>
            <p className="mt-1 text-xs text-[var(--color-text-muted)]">You dealt with it outside ServiceHub. Give a reason; it is recorded.</p>
            {!settling && <button type="button" onClick={() => setSettling(true)} disabled={!may.allowed} title={may.reason ?? undefined} className="mt-2 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-amber-900 disabled:opacity-50">Mark as settled…</button>}
          </div>
          <div className="rounded-xl border border-[var(--color-border)] p-3 text-sm">
            <p className="flex items-center font-bold">Leave it<Tip label="choice:Leave it" /></p>
            <p className="mt-1 text-xs text-[var(--color-text-muted)]">It stays in the queue and stays on this list.</p>
            <button type="button" onClick={close} className="mt-2 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold">Leave it</button>
          </div>
        </div>
        {!may.allowed && <NotAllowed reason={may.reason} />}
        {settling && (
          <div className="mt-3 space-y-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm">
            <label htmlFor="settle-found" className="block font-semibold">What did you find? <span className="font-normal text-[var(--color-text-muted)]">(required)</span></label>
            <textarea id="settle-found" value={found} onChange={(e) => setFound(e.target.value)} maxLength={400} rows={3} placeholder="e.g. the main queue holds one copy, so nothing more needs sending"
              className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2" />
            <p className="text-xs text-[var(--color-text-muted)]">Recorded with your name. The attempt is closed as written off, not as recovered or failed.</p>
            {resolve.isError && <p role="alert" className="text-red-800">That didn’t go through: {toProblem(resolve.error).message} Nothing was recorded.</p>}
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setSettling(false)} disabled={resolve.isPending} className="rounded-lg border border-[var(--color-border)] bg-white px-3 py-1.5 font-semibold">Cancel</button>
              <button type="button" disabled={!found.trim() || resolve.isPending || !may.allowed} onClick={() => resolve.mutate({ entryId: item.entryId as string, reason: found.trim() })}
                className="rounded-lg bg-amber-700 px-3.5 py-1.5 font-bold text-white disabled:opacity-50">{resolve.isPending ? 'Recording…' : 'Record what I found'}</button>
            </div>
          </div>
        )}
      </section>

      <section aria-label="What happened so far">
        <h3 className="mb-2 text-sm font-bold">What happened so far<Tip label="section:What happened so far" /></h3>
        {entry.isError ? <p className="text-sm text-[var(--color-text-muted)]">The steps could not be read.</p>
          : events.length === 0 ? <p className="text-sm text-[var(--color-text-muted)]">{entry.isPending ? 'Reading the steps…' : 'No steps are recorded.'}</p>
          : (
            <ol className="space-y-2 text-sm">
              {[...events].reverse().map((ev) => {
                const w = describeEvent(ev, cloud)
                return (
                  <li key={ev.seq} className="flex items-start justify-between gap-3 border-b border-[var(--color-border)] pb-2">
                    <span><span className="font-medium">{w.title}</span>{w.note && <span className="block text-xs text-[var(--color-text-muted)]">{w.note}</span>}<span className="block text-xs text-[var(--color-text-muted)]">{ev.actor.label}</span></span>
                    <time dateTime={ev.occurredAt} className="shrink-0 text-xs text-[var(--color-text-muted)]">{formatWhen(ev.occurredAt, now)}</time>
                  </li>
                )
              })}
            </ol>
          )}
      </section>
    </div>
  )
}
