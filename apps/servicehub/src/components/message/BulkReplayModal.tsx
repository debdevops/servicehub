import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, CircleStop, Eye, Play, ShieldCheck, TriangleAlert } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import type { OverlayBodyProps } from '../overlays/registry'
import { cancelBulk, fetchBulk, isEnded, previewBulk, startBulk, type BulkPreview, type BulkProgress } from '../../lib/api/bulk'
import { fetchDeadLetters } from '../../lib/api/deadLetters'
import { BULK_LIMIT, bulkSelection } from '../../lib/bulkSelection'
import { deadLetterKeys } from '../../hooks/useDeadLetters'
import { replayKeys } from '../../hooks/useReplay'

/** Preview · Run · Watch — the same three steps in both views, so a person always knows where they are. */
function Steps({ at }: { at: 1 | 2 | 3 }) {
  const step = (n: number, label: string) => (
    <li key={n} className="flex items-center gap-2">
      <span
        aria-hidden="true"
        className={`flex h-6 w-6 items-center justify-center rounded-full text-[11px] font-bold ${n < at ? 'bg-[var(--color-success)] text-white' : n === at ? 'bg-[var(--color-primary-600)] text-white' : 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]'}`}
      >
        {n < at ? <Check className="h-3.5 w-3.5" /> : n}
      </span>
      <span className={`text-[13px] ${n === at ? 'font-semibold text-[var(--color-primary-700)]' : 'text-[var(--color-text-muted)]'}`}>{label}</span>
    </li>
  )
  return <ol aria-label="Steps" className="flex items-center gap-4">{[step(1, 'Preview'), step(2, 'Run'), step(3, 'Watch')]}</ol>
}

/** Looks a validation-type failure up in words: those usually fail again unless the sender was fixed. */
const looksLikeValidation = (reason: string) => /valid|schema|format|malformed|missing/i.test(reason)

/**
 * Bulk Replay (`?modal=bulk-replay`): the preview, then the run, without leaving the page. There is no way to reach the run
 * without the preview — starting needs the stored preview's id, and only this modal makes one. Closing it does NOT stop
 * a running job: the run is the server's, and progress reappears from `?job=`.
 */
export default function BulkReplayModal({ close }: OverlayBodyProps) {
  const [params, setParams] = useSearchParams()
  const jobId = params.get('job')
  return jobId ? <Running id={jobId} close={close} /> : <Preview close={close} onStarted={(id) => setParams((c) => { const n = new URLSearchParams(c); n.set('job', id); return n }, { replace: true })} />
}

function Preview({ close, onStarted }: { close: () => void; onStarted: (id: string) => void }) {
  const [state, setState] = useState<{ preview: BulkPreview } | { error: string } | { empty: true } | null>(null)
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string | null>(null)
  const ran = useRef(false)

  useEffect(() => {
    if (ran.current) return
    ran.current = true
    void (async () => {
      const selection = bulkSelection.get()
      if (!selection) return setState({ empty: true })
      try {
        let ids: number[]
        if ('ids' in selection) ids = [...selection.ids]
        else {
          ids = []
          const pages = Math.ceil(Math.min(selection.total, BULK_LIMIT) / 100)
          for (let page = 1; page <= pages; page++) ids.push(...(await fetchDeadLetters({ ...selection.query, page })).items.map((m) => m.id))
        }
        if (ids.length === 0) return setState({ empty: true })
        setState({ preview: await previewBulk(ids.slice(0, BULK_LIMIT)) })
      } catch {
        setState({ error: 'ServiceHub couldn’t work out what this would do, so nothing was sent.' })
      }
    })()
  }, [])

  const start = async (preview: BulkPreview, sampleOnly: boolean) => {
    setStarting(true)
    setStartError(null)
    try {
      onStarted((await startBulk(preview.previewId, sampleOnly)).id)
    } catch {
      setStartError('This preview can no longer be started — make a new one. Nothing was sent.')
      setStarting(false)
    }
  }

  if (state === null) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Working out what replaying these would do…</p>
  if ('empty' in state) {
    return <p className="text-sm text-[var(--color-text-muted)]">Choose some dead letters in the table first, then Replay selected. <button type="button" onClick={close} className="font-medium text-[var(--color-primary-700)] hover:underline">Close</button></p>
  }
  if ('error' in state) return <p role="alert" className="text-sm text-[var(--color-error)]">{state.error}</p>

  const p = state.preview
  const sampleAdvised = p.willReplay > 1 && p.groups.length > 0 && p.groups.filter((g) => g.willReplay > 0).every((g) => looksLikeValidation(g.reason))

  return (
    <div className="space-y-5">
      <div>
        <h3 className="text-lg font-bold">Replay {p.selected.toLocaleString()} {p.selected === 1 ? 'message' : 'messages'}</h3>
        <div className="mt-2"><Steps at={1} /></div>
        <p className="mt-2 inline-flex items-center gap-1.5 rounded-full bg-[var(--color-surface-muted)] px-2.5 py-0.5 text-[11px] font-bold uppercase tracking-wide"><Eye className="h-3 w-3" aria-hidden="true" /> Preview — nothing has run yet</p>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <Box value={p.selected} label="selected" tone="plain" />
        <Box value={p.willReplay} label="will be replayed" tone="green" />
        <Box value={p.heldBackCount} label="held back — stays in dead letters" tone="amber" />
      </div>

      <section aria-label="Grouped by how they failed">
        <h4 className="mb-2 text-[11px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Grouped by how they failed</h4>
        <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
          {p.groups.map((g) => (
            <li key={g.reason} className="flex items-center gap-3 px-3.5 py-2.5 text-sm">
              <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{g.reason}</span>
              <span className="text-[var(--color-text-muted)]">{g.willReplay} will be replayed{g.heldBack > 0 ? `, ${g.heldBack} held back` : ''}</span>
              <span className="tabular ml-auto font-bold">{g.selected}</span>
            </li>
          ))}
        </ul>
      </section>

      {p.heldBack.length > 0 && (
        <section aria-label="Held back" className="space-y-2">
          {p.heldBack.map((h) => (
            <div key={h.dlqMessageId} className="flex items-start gap-2.5 rounded-xl border border-[#fde68a] bg-[var(--color-warning-light)] px-3.5 py-2.5 text-[13px] text-[#78350f]">
              <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-[#d97706]" aria-hidden="true" />
              <span><b>Message {h.dlqMessageId} held back</b> — {h.remedy} <span className="font-mono text-[11px] opacity-70">{h.reasonCode}</span></span>
            </div>
          ))}
        </section>
      )}

      {sampleAdvised && (
        <p className="flex items-start gap-2.5 rounded-xl border border-[#fde68a] bg-[var(--color-warning-light)] px-3.5 py-2.5 text-[13px] text-[#78350f]">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-[#d97706]" aria-hidden="true" />
          <span>
            These all failed on validation, and usually fail again unless the sender was fixed. <b>Replay one first?</b>{' '}
            <button type="button" disabled={starting} onClick={() => void start(p, true)} className="font-semibold text-[var(--color-primary-700)] hover:underline">Replay a sample of 1 ›</button>
          </span>
        </p>
      )}

      <section aria-label="How it will run">
        <h4 className="mb-2 text-[11px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">How it will run</h4>
        <div className="grid grid-cols-2 gap-3">
          <Info title="Pace">{p.perSecond} {p.perSecond === 1 ? 'message' : 'messages'} a second — gentle on your consumer.</Info>
          <Info title="Stops by itself if…">{p.stopAfterConsecutiveFailures} sends in a row aren’t accepted.</Info>
        </div>
      </section>

      {startError && <p role="alert" className="text-sm text-[var(--color-error)]">{startError}</p>}

      <footer className="flex items-center gap-3 border-t border-[var(--color-border)] pt-4">
        <span className="flex items-center gap-1.5 text-[13px] text-[var(--color-text-muted)]"><ShieldCheck className="h-4 w-4" aria-hidden="true" /> Each message is checked again as it is sent.</span>
        <button type="button" onClick={close} className="ml-auto rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Cancel</button>
        <button
          type="button"
          disabled={p.willReplay === 0 || starting}
          onClick={() => void start(p, false)}
          className="flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white disabled:opacity-50"
        >
          <Play className="h-4 w-4" aria-hidden="true" /> Replay {p.willReplay.toLocaleString()} {p.willReplay === 1 ? 'message' : 'messages'}
        </button>
      </footer>
    </div>
  )
}

function Running({ id, close }: { id: string; close: () => void }) {
  const client = useQueryClient()
  const { data, isError } = useQuery({
    queryKey: ['bulk', id],
    queryFn: () => fetchBulk(id),
    refetchInterval: (q) => (q.state.data && isEnded(q.state.data.status) ? false : 1000),
  })

  const ended = data ? isEnded(data.status) : false
  useEffect(() => {
    if (ended) {
      void client.invalidateQueries({ queryKey: deadLetterKeys.all })
      void client.invalidateQueries({ queryKey: replayKeys.all })
    }
  }, [ended, client])

  if (isError) return <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn’t read this bulk replay just now. It keeps running; close this and look in Replayed.</p>
  if (!data) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading progress…</p>
  return <Progress p={data} close={close} />
}

function Progress({ p, close }: { p: BulkProgress; close: () => void }) {
  const [stopping, setStopping] = useState(false)
  const target = p.sampleOnly ? Math.min(1, p.willReplay) : p.willReplay
  const done = p.sent + p.failed + p.unknown
  const pct = target === 0 ? 0 : Math.min(100, Math.round((done / target) * 100))
  const ended = isEnded(p.status)
  const heading = ended
    ? p.status === 'completed' ? 'Bulk replay finished' : p.status === 'cancelled' ? 'Bulk replay stopped' : 'Bulk replay stopped itself'
    : `Replaying ${target.toLocaleString()} ${target === 1 ? 'message' : 'messages'}`

  return (
    <div className="space-y-5">
      <div>
        <h3 className="text-lg font-bold">{heading}</h3>
        <div className="mt-2"><Steps at={ended ? 3 : 2} /></div>
      </div>

      <div>
        <div className="flex items-baseline justify-between text-sm">
          <b>{done.toLocaleString()} of {target.toLocaleString()} sent</b>
          {!ended && <span className="text-[var(--color-text-muted)]">{p.remaining.toLocaleString()} to go</span>}
        </div>
        <div className="mt-2 h-2.5 overflow-hidden rounded-full bg-[var(--color-surface-muted)]" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Bulk replay progress">
          <div className="h-full rounded-full bg-[var(--color-primary-600)] transition-all" style={{ width: `${pct}%` }} />
        </div>
      </div>

      <ul className="space-y-2 text-sm">
        <Row ok={p.failed === 0}>{p.sent.toLocaleString()} accepted by the cloud</Row>
        <Row ok={p.failed === 0}>{p.failed.toLocaleString()} failed to send</Row>
        {p.unknown > 0 && <Row ok={false}>{p.unknown.toLocaleString()} unknown — ServiceHub lost contact; check the queue before trying again</Row>}
        {p.heldBack > 0 && <Row ok>{p.heldBack.toLocaleString()} held back — stayed in dead letters</Row>}
      </ul>

      {p.endedReason && <p className="rounded-xl border border-[#fde68a] bg-[var(--color-warning-light)] px-3.5 py-2.5 text-[13px] text-[#78350f]">{p.endedReason}</p>}

      <p className="flex items-start gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-3.5 py-2.5 text-[13px]">
        <Eye className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
        <span>Close this whenever you like. Progress and the final verdict appear in <b>Replayed</b>, and in the bell if anything needs you.</span>
      </p>

      <footer className="flex items-center gap-3 border-t border-[var(--color-border)] pt-4">
        {!ended && (
          <button
            type="button"
            disabled={stopping || p.status !== 'running'}
            onClick={() => { setStopping(true); void cancelBulk(p.id) }}
            className="flex items-center gap-2 rounded-lg border border-[#fecaca] px-4 py-2 text-sm font-semibold text-[#b91c1c] disabled:opacity-50"
          >
            <CircleStop className="h-4 w-4" aria-hidden="true" /> {stopping ? 'Stopping…' : 'Stop now'}
          </button>
        )}
        <button type="button" onClick={close} className="ml-auto rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Close</button>
      </footer>
    </div>
  )
}

function Box({ value, label, tone }: { value: number; label: string; tone: 'plain' | 'green' | 'amber' }) {
  const cls = tone === 'green' ? 'border-[#a7f3d0] bg-[#ecfdf5]' : tone === 'amber' ? 'border-[#fde68a] bg-[#fffbeb]' : 'border-[var(--color-border)]'
  return (
    <div className={`rounded-xl border px-3.5 py-3 ${cls}`}>
      <div className="tabular text-2xl font-extrabold leading-none">{value.toLocaleString()}</div>
      <div className="mt-1 text-[12.5px] text-[var(--color-text-muted)]">{label}</div>
    </div>
  )
}

function Info({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-[var(--color-border)] px-3.5 py-3">
      <div className="text-sm font-bold">{title}</div>
      <div className="mt-1 text-[13px] text-[var(--color-text-muted)]">{children}</div>
    </div>
  )
}

function Row({ ok, children }: { ok: boolean; children: React.ReactNode }) {
  return (
    <li className="flex items-center gap-2.5">
      <span aria-hidden="true" className={`flex h-5 w-5 items-center justify-center rounded-full ${ok ? 'bg-[var(--color-success-light)] text-[#047857]' : 'bg-[var(--color-warning-light)] text-[#92400e]'}`}>
        {ok ? <Check className="h-3 w-3" /> : <TriangleAlert className="h-3 w-3" />}
      </span>
      {children}
    </li>
  )
}
