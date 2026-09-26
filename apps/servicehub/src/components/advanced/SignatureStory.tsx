import { useQuery } from '@tanstack/react-query'
import { Search } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { CloudProvider } from '../../lib/api/namespaces'
import { fetchIncident, fetchTrace } from '../../lib/api/signatures'
import { formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'

const dot: Readonly<Record<string, string>> = { first_seen: '#ef4444', came_back: '#fca5a5', replayed: '#0284c7', purged: '#6b7280', dead_lettered: '#ef4444' }

/** Incident (unit 6.19): one signature's whole story, newest first. Read-only; every entry links to its evidence. */
export function IncidentTimeline({ hash, provider }: { hash: string; provider: CloudProvider }) {
  const q = useQuery({ queryKey: ['signatures', 'incident', hash, provider], queryFn: () => fetchIncident(hash, provider) })
  const now = new Date()
  if (q.isPending) return <p role="status" className="text-[var(--color-text-muted)]">Reading its story…</p>
  if (q.isError) return <p role="alert">ServiceHub couldn’t read this signature’s story.</p>
  const s = q.data
  return (
    <div className="space-y-3">
      <p className="text-[12.5px] text-[var(--color-text-muted)]">
        {s.messages} dead-lettered · {s.stillStuck} still stuck · {s.replays} replayed, {s.stayedFixed} stayed fixed{s.cameBackAfterReplay > 0 ? `, ${s.cameBackAfterReplay} came back` : ''}
      </p>
      <ol aria-label="Incident timeline" className="relative space-y-3 border-l border-[var(--color-border)] pl-4">
        {s.timeline.map((i, n) => (
          <li key={n} className="relative">
            <span aria-hidden="true" className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full" style={{ background: dot[i.kind] }} />
            <p className="text-[11.5px] text-[var(--color-text-muted)]">{formatWhen(i.at, now)}</p>
            <p>{i.text} {i.entryId && <Link to={`/advanced/ledger?entry=${i.entryId}&window=all`} className="font-medium text-[var(--color-primary-700)] hover:underline">Evidence ›</Link>}</p>
          </li>
        ))}
      </ol>
    </div>
  )
}

/** Trace (unit 6.19): search a correlation id; every recorded sighting across connected clouds, oldest first. */
export function TraceView() {
  const [draft, setDraft] = useState('')
  const [id, setId] = useState('')
  const q = useQuery({ queryKey: ['trace', id], queryFn: () => fetchTrace(id), enabled: id !== '' })
  const now = new Date()
  return (
    <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5 shadow-[var(--shadow-card)]">
      <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); setId(draft.trim()) }}>
        <label className="flex flex-1 items-center gap-2 rounded-lg border border-[var(--color-border)] px-3 py-2">
          <Search className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
          <span className="sr-only">Correlation id</span>
          <input value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="A correlation id, e.g. order-42" className="w-full bg-transparent text-sm outline-none" />
        </label>
        <button type="submit" disabled={!draft.trim()} className="rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">Trace</button>
      </form>
      <p className="mt-2 text-xs text-[var(--color-text-muted)]">Searches what ServiceHub recorded across every connected cloud — it never looks into a cloud to trace, so tracing can’t cause a delivery.</p>
      {q.isFetching && <p role="status" className="mt-4 text-sm text-[var(--color-text-muted)]">Tracing…</p>}
      {q.isError && <p role="alert" className="mt-4 text-sm">ServiceHub couldn’t trace that just now.</p>}
      {q.data && q.data.hops.length === 0 && <p className="mt-4 text-sm">Nothing recorded carries “{q.data.correlationId}”. {q.data.note}</p>}
      {q.data && q.data.hops.length > 0 && (
        <>
          <p className="mt-4 text-sm"><b>{q.data.hops.length}</b> recorded {q.data.hops.length === 1 ? 'sighting' : 'sightings'} across <b>{q.data.clouds.map((c) => providerLabel[c as CloudProvider] ?? c).join(' and ')}</b>, oldest first.</p>
          <ol aria-label="Trace" className="mt-2 divide-y divide-[var(--color-border)] text-sm">
            {q.data.hops.map((h, i) => (
              <li key={i} className="flex flex-wrap items-baseline gap-x-3 py-2">
                <span className="w-36 text-xs text-[var(--color-text-muted)]">{formatWhen(h.at, now)}</span>
                <span className="w-24 font-semibold">{providerLabel[h.place.provider as CloudProvider] ?? h.place.provider}</span>
                <span className="min-w-0 flex-1">
                  {h.kind === 'dead_lettered' ? 'Dead-lettered' : h.kind === 'purged' ? 'Purged' : 'Replayed'} on <span className="font-mono">{h.entity}</span> in {h.place.namespaceName}{h.detail ? ` — ${h.detail}` : ''}
                </span>
                {h.kind === 'dead_lettered' && h.dlqMessageId !== null && <Link to={`/?tab=dlq&ns=${h.namespaceId}&message=${h.dlqMessageId}&status=all`} className="text-xs font-medium text-[var(--color-primary-700)] hover:underline">Open ›</Link>}
                {h.entryId && <Link to={`/advanced/ledger?entry=${h.entryId}&window=all`} className="text-xs font-medium text-[var(--color-primary-700)] hover:underline">Evidence ›</Link>}
              </li>
            ))}
          </ol>
        </>
      )}
    </div>
  )
}
