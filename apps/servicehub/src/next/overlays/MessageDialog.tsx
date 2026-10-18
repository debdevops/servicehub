import { useState, type ReactNode } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { BodyBlock, Properties } from '../../components/message/MessageDrawer'
import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import { useDeadLetter } from '../../hooks/useDeadLetter'
import { useMe } from '../../hooks/useIdentity'
import { useNamespace } from '../../hooks/useNamespaces'
import { usePendingWork } from '../../hooks/usePendingWork'
import { useReplays } from '../../hooks/useReplay'
import type { DeadLetterDetail } from '../../lib/api/deadLetters'
import { formatBytes, formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { usePreferences } from '../../lib/preferences'
import { findTrace, traceLink } from '../../lib/trace'
import { entityLabel } from '../lib/entityRows'
import { messageIdOf } from '../lib/messageId'
import type { NextOverlayBodyProps } from './registry'

type Tab = 'summary' | 'body' | 'properties' | 'history'
const tabs: readonly { id: Tab; label: string }[] = [
  { id: 'summary', label: 'Summary' }, { id: 'body', label: 'Body' }, { id: 'properties', label: 'Properties' }, { id: 'history', label: 'History' },
]

function Row({ label, children }: { label: string; children: ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 text-sm"><dt className="w-36 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 break-words font-semibold">{children}</dd></div>
}

/**
 * One dead letter in full (build-plan unit D3; design `d07-message-full-view`), at `?dialog=message&id=`. Read-only: it never changes
 * a message, and a replay sends exactly what the Body tab shows. Summary → Body → Properties → History. Where the message stands comes
 * from the same pending-work read as every attention table, so the two never disagree.
 */
export default function MessageDialog({ close }: NextOverlayBodyProps) {
  const [params] = useSearchParams()
  const { pathname } = useLocation()
  const id = messageIdOf(params.get('id'))
  const detail = useDeadLetter(id)
  const namespace = useNamespace(detail.data?.item.namespaceId)
  const pending = usePendingWork(detail.data ? { namespaceId: detail.data.item.namespaceId } : {})
  const replays = useReplays({ dlqMessageId: id ?? undefined, pageSize: 25 })
  const me = useMe()
  const { traceLink: template } = usePreferences()
  const [tab, setTab] = useState<Tab>('summary')
  useOverlayTitle(detail.data ? `Message ${detail.data.item.messageId.length > 14 ? `${detail.data.item.messageId.slice(0, 6)}…${detail.data.item.messageId.slice(-6)}` : detail.data.item.messageId}` : 'Message')

  if (id === null) return <p role="alert" className="text-sm">This address names no message.</p>
  if (detail.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
  if (detail.isError) return <p role="alert" className="text-sm">That message could not be read. It may no longer be recorded.</p>

  const d: DeadLetterDetail = detail.data
  const m = d.item
  const held = (pending.data?.items ?? []).find((i) => i.dlqMessageId === m.id)
  const state = held ? (held.kind === 'unresolved' ? 'Handed to a person' : 'Waiting for approval') : m.status === 'active' ? 'In dead-letter' : 'No longer in dead-letter'
  const may = permission(me.data, 'Approver', 'approve or decline a replay', { recover: true, namespaceId: m.namespaceId })
  const trace = findTrace(d.applicationPropertiesJson)
  const link = trace ? traceLink(template, trace.traceId) : null
  const now = new Date()
  const history = [
    ...(replays.data?.items ?? []).map((r) => ({ at: r.replayedAt, what: `Replayed by ${r.replayedBy} to ${r.targetEntity} — ${r.outcomeStatus === 'accepted' ? 'accepted by the cloud' : r.outcomeStatus === 'rejected' ? 'refused by the cloud' : 'no recorded answer'}` })),
    ...(held ? [{ at: held.since, what: held.kind === 'unresolved' ? 'A replay was attempted and has no recorded answer' : 'The Agent asked for approval to replay it' }] : []),
    { at: m.detectedAtUtc, what: 'ServiceHub recorded it in dead-letter' },
    ...(d.resolvedAt ? [{ at: d.resolvedAt, what: 'It was no longer in dead-letter' }] : []),
  ].sort((a, b) => b.at.localeCompare(a.at))

  return (
    <div className="space-y-4">
      <p className="text-sm text-[var(--color-text-muted)]">{[namespace.data?.displayName ?? namespace.data?.name, entityLabel(m.entityName, m.topicName), state].filter(Boolean).join(' · ')}</p>
      <nav aria-label="Sections of this message" className="flex gap-1 border-b border-[var(--color-border)]">
        {tabs.map((t) => (
          <button key={t.id} type="button" aria-current={tab === t.id ? 'page' : undefined} onClick={() => setTab(t.id)}
            className={`-mb-px border-b-2 px-3 py-2 text-sm font-semibold ${tab === t.id ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)]'}`}>{t.label}</button>
        ))}
      </nav>

      {tab === 'summary' && (
        <div className="grid gap-6 lg:grid-cols-2">
          <div className="space-y-4">
            <section aria-label="Why it failed">
              <h3 className="mb-1 text-sm font-bold">Why it failed</h3>
              <dl>
                <Row label="Reason">{m.deadLetterReason ?? 'No reason recorded'}</Row>
                <Row label="Description">{m.deadLetterErrorDescription ?? '—'}</Row>
                <Row label="Others like it">{d.othersLikeIt === 0 ? 'None in this queue with the same reason' : `${d.othersLikeIt.toLocaleString('en-US')} more in this queue with the same reason`}</Row>
                <Row label="Deliveries">{m.deliveryCount}</Row>
                <Row label="Enqueued">{formatWhen(m.enqueuedTimeUtc, now)}</Row>
                <Row label="Detected">{formatWhen(m.detectedAtUtc, now)}</Row>
              </dl>
            </section>
            <BodyBlock detail={d} field={null} />
          </div>
          <div className="space-y-4">
            <section aria-label="Message properties">
              <h3 className="mb-1 text-sm font-bold">Message details</h3>
              <dl>
                <Row label="Message ID"><span className="font-mono text-xs">{m.messageId}</span></Row>
                <Row label="Sequence number">{m.sequenceNumber.toLocaleString('en-US')}</Row>
                <Row label="Correlation ID"><span className="font-mono text-xs">{d.correlationId ?? 'None'}</span></Row>
                <Row label="Session">{d.sessionId ?? 'None'}</Row>
                <Row label="Content type">{d.contentType ?? 'Not recorded'}</Row>
                <Row label="Size">{formatBytes(m.sizeInBytes)}</Row>
              </dl>
            </section>
            <Properties json={d.applicationPropertiesJson} />
          </div>
        </div>
      )}
      {tab === 'body' && <BodyBlock detail={d} field={null} />}
      {tab === 'properties' && <Properties json={d.applicationPropertiesJson} />}
      {tab === 'history' && (
        <section aria-label="History">
          <h3 className="mb-1 text-sm font-bold">History</h3>
          <table className="w-full text-sm">
            <caption className="sr-only">What happened to this message</caption>
            <thead><tr className="bg-[var(--color-surface-muted)] text-left text-xs font-semibold text-[var(--color-text-muted)]"><th className="px-3 py-2">When</th><th className="px-3 py-2">What</th></tr></thead>
            <tbody>{history.map((h, i) => <tr key={i} className="border-t border-[var(--color-border)]"><td className="whitespace-nowrap px-3 py-2">{formatWhen(h.at, now)}</td><td className="px-3 py-2">{h.what}</td></tr>)}</tbody>
          </table>
        </section>
      )}

      <p role="note" className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-2.5 text-sm"><strong>Read-only.</strong> ServiceHub never changes a message’s body. A replay sends exactly what is shown here.</p>
      <p className="text-xs text-[var(--color-text-muted)]">What ServiceHub keeps of this message on this machine: a fingerprint of the body and the first 500 characters, as plain text.{d.bodyIsPreview ? ' The body above is that kept preview.' : ''}</p>

      <div className="flex flex-wrap items-center justify-end gap-2 border-t border-[var(--color-border)] pt-3">
        {link && <a href={link} target="_blank" rel="noreferrer" className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold hover:bg-[var(--color-surface-muted)]">Open the trace</a>}
        {held && held.kind === 'approval' && (may.allowed
          ? <Link to={`${pathname}?dialog=review&entry=${encodeURIComponent(held.entryId ?? held.id)}`} className="rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-bold text-white hover:bg-[var(--color-primary-700)]">Review…</Link>
          : <span title={may.reason ?? undefined} className="text-sm text-[var(--color-text-muted)]">Needs an Approver to review</span>)}
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-sm font-semibold hover:bg-[var(--color-surface-muted)]">Close</button>
      </div>
    </div>
  )
}
