import { useQueryClient } from '@tanstack/react-query'
import { Check, Eye, LoaderCircle, Minimize2, Play, ShieldCheck, TriangleAlert, X } from 'lucide-react'
import { aggregate, closeReplayAll, minimizeReplayAll, restoreReplayAll, startReplayAll, stopReplayAll, useReplayAll, type ReplayAllState } from '../../lib/replayAll'
import { OverlayFrame } from '../overlays/OverlayFrame'
import { Box, Progress, Steps } from './BulkReplayModal'

/**
 * The Replay all window and its minimized corner chip. Mounted once in the app layout — not tied to the URL or to a page — so
 * a run carries on, and can be reopened, wherever the person navigates.
 */
export function ReplayAllDock() {
  const s = useReplayAll()
  if (!s) return null
  return s.minimized ? <Chip s={s} /> : <Window s={s} />
}

function Window({ s }: { s: ReplayAllState }) {
  const client = useQueryClient()
  const live = s.phase === 'running'
  const minimize = (
    <button type="button" aria-label="Minimize" title="Minimize — it keeps running" onClick={minimizeReplayAll} className="rounded-lg p-1.5 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]">
      <Minimize2 className="h-4 w-4" aria-hidden="true" />
    </button>
  )
  return (
    <OverlayFrame kind="modal" size="wide" title="Replay all messages" description={s.scopeLabel} onClose={closeReplayAll} actions={s.phase === 'error' ? undefined : minimize}>
      {s.phase === 'preparing' && <p role="status" className="text-sm text-[var(--color-text-muted)]">Finding everything that is stuck, and working out what replaying it would do…</p>}
      {s.phase === 'error' && (
        <div className="space-y-4">
          <p role="alert" className="text-sm text-[var(--color-error)]">{s.error}</p>
          <button type="button" onClick={closeReplayAll} className="rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Close</button>
        </div>
      )}
      {s.phase === 'ready' && <Ready s={s} onStart={() => void startReplayAll(client)} />}
      {(s.phase === 'running' || s.phase === 'ended') && <Progress p={aggregate(s)} close={closeReplayAll} onStop={stopReplayAll} onMinimize={live ? minimizeReplayAll : undefined} />}
    </OverlayFrame>
  )
}

function Ready({ s, onStart }: { s: ReplayAllState; onStart: () => void }) {
  const a = aggregate(s)
  const groups = new Map<string, { selected: number; willReplay: number; heldBack: number }>()
  for (const c of s.chunks) for (const g of c.preview.groups) {
    const t = groups.get(g.reason) ?? { selected: 0, willReplay: 0, heldBack: 0 }
    groups.set(g.reason, { selected: t.selected + g.selected, willReplay: t.willReplay + g.willReplay, heldBack: t.heldBack + g.heldBack })
  }
  const held = s.chunks.flatMap((c) => c.preview.heldBack)
  const first = s.chunks[0]!.preview
  const runs = s.chunks.length
  return (
    <div className="space-y-5">
      <div>
        <h3 className="text-lg font-bold">Replay all · {a.selected.toLocaleString()} {a.selected === 1 ? 'message' : 'messages'}</h3>
        <div className="mt-2"><Steps at={1} /></div>
        <p className="mt-2 text-[13px] text-[var(--color-text-muted)]">This is everything still stuck in <b>{s.scopeLabel}</b>. The filters, time window and search on the list do not apply.</p>
        {s.cloudCount !== null && s.cloudCount > a.selected && (
          <p className="mt-1.5 text-[13px] text-[#78350f]">ServiceHub has recorded <b>{a.selected.toLocaleString()}</b> of the <b>{s.cloudCount.toLocaleString()}</b> this cloud counts. Only recorded messages can be replayed; use <b>Look now</b> to record more.</p>
        )}
        <p className="mt-2 inline-flex items-center gap-1.5 rounded-full bg-[var(--color-surface-muted)] px-2.5 py-0.5 text-[11px] font-bold uppercase tracking-wide"><Eye className="h-3 w-3" aria-hidden="true" /> Preview — nothing has run yet</p>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <Box value={a.selected} label="stuck in the dead-letter queue" tone="plain" />
        <Box value={a.willReplay} label="will be replayed" tone="green" />
        <Box value={a.heldBack} label="held back — stays in dead letters" tone="amber" />
      </div>

      <ul aria-label="Grouped by how they failed" className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
        {[...groups].map(([reason, g]) => (
          <li key={reason} className="flex items-center gap-3 px-3.5 py-2.5 text-sm">
            <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{reason}</span>
            <span className="text-[var(--color-text-muted)]">{g.willReplay} will be replayed{g.heldBack > 0 ? `, ${g.heldBack} held back` : ''}</span>
            <span className="tabular ml-auto font-bold">{g.selected}</span>
          </li>
        ))}
      </ul>

      {held.length > 0 && (
        <p className="flex items-start gap-2.5 rounded-xl border border-[#fde68a] bg-[var(--color-warning-light)] px-3.5 py-2.5 text-[13px] text-[#78350f]">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-[#d97706]" aria-hidden="true" />
          <span><b>{held.length.toLocaleString()} held back.</b> {held[0]!.remedy} They stay in dead letters; each one is listed when the run ends.</span>
        </p>
      )}

      <div className="grid grid-cols-2 gap-3">
        <Info title="Pace">{first.perSecond} {first.perSecond === 1 ? 'message' : 'messages'} a second, one by one — gentle on your consumer.{runs > 1 ? ` ${a.selected.toLocaleString()} messages go back in ${runs} runs, one after another.` : ''}</Info>
        <Info title="Stops by itself if…">{first.stopAfterConsecutiveFailures} sends in a row aren’t accepted.</Info>
      </div>

      <p className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-3.5 py-2.5 text-[13px]">
        It runs in the background: <b>minimize</b> this window and carry on elsewhere. You’ll get a notice when it finishes{runs > 1 ? ' — keep this browser tab open until then, because the next run is started from here' : ''}.
      </p>

      <footer className="sticky bottom-0 z-10 -mx-5 -mb-4 flex flex-wrap items-center gap-3 border-t border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-3">
        <span className="flex items-center gap-1.5 text-[13px] text-[var(--color-text-muted)]"><ShieldCheck className="h-4 w-4" aria-hidden="true" /> Each message is checked again as it is sent.</span>
        <button type="button" onClick={closeReplayAll} className="ml-auto rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Cancel</button>
        <button type="button" disabled={a.willReplay === 0} onClick={onStart} className="flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">
          <Play className="h-4 w-4" aria-hidden="true" /> Replay {a.willReplay.toLocaleString()} {a.willReplay === 1 ? 'message' : 'messages'}
        </button>
      </footer>
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

/** Bottom-right while minimized: live count while running, the verdict once done. Click to reopen the window. */
function Chip({ s }: { s: ReplayAllState }) {
  const a = aggregate(s)
  const done = a.sent + a.failed + a.unknown
  const ended = s.phase === 'ended'
  const pct = a.willReplay === 0 ? 0 : Math.min(100, Math.round((done / a.willReplay) * 100))
  const problem = ended && (a.failed + a.unknown > 0 || s.status !== 'completed')
  const label = ended
    ? s.status === 'cancelled' ? 'Replay all stopped' : problem ? 'Replay all finished with problems' : 'Replay all finished'
    : s.phase === 'running' ? `Replaying ${done.toLocaleString()} of ${a.willReplay.toLocaleString()}` : s.phase === 'error' ? 'Replay all couldn’t start' : 'Replay all — getting ready'
  return (
    <div className="fixed bottom-5 right-5 z-50 flex w-72 items-stretch overflow-hidden rounded-2xl bg-[#0f172a] text-white shadow-2xl">
      <button type="button" onClick={restoreReplayAll} aria-label={`${label}. Open the replay-all window`} className="flex min-w-0 flex-1 items-center gap-3 p-3 text-left">
        <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${ended ? (problem ? 'bg-[#b45309]' : 'bg-[#047857]') : 'bg-[var(--color-primary-600)]'}`}>
          {ended ? (problem ? <TriangleAlert className="h-4 w-4" aria-hidden="true" /> : <Check className="h-4 w-4" aria-hidden="true" />) : <LoaderCircle className="h-4 w-4 animate-spin" aria-hidden="true" />}
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-bold">{label}</span>
          {s.phase === 'running' ? (
            <span className="mt-1.5 block h-1.5 overflow-hidden rounded-full bg-white/20" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Replay all progress"><span className="block h-full rounded-full bg-white transition-all" style={{ width: `${pct}%` }} /></span>
          ) : (
            <span className="block text-xs text-slate-300">{ended ? `${a.sent.toLocaleString()} sent back · click to view` : 'Click to open'}</span>
          )}
        </span>
      </button>
      {s.phase !== 'running' && (
        <button type="button" onClick={closeReplayAll} aria-label="Dismiss" className="px-3 text-slate-400 hover:text-white"><X className="h-4 w-4" aria-hidden="true" /></button>
      )}
    </div>
  )
}
