import { useSyncExternalStore } from 'react'
import type { QueryClient } from '@tanstack/react-query'
import { cancelBulk, fetchBulk, isEnded, previewBulk, startBulk, type BulkPreview, type BulkProgress, type BulkStatus } from './api/bulk'
import { fetchDeadLetters, type DeadLetterQuery } from './api/deadLetters'
import { bulkSelection, BULK_LIMIT } from './bulkSelection'
import { showNotice } from './notice'
import { deadLetterKeys } from '../hooks/useDeadLetters'
import { replayKeys } from '../hooks/useReplay'

/**
 * Replay all messages: every message stuck in the scope being looked at, replayed one by one in the background.
 *
 * The server caps one bulk run at BULK_LIMIT messages, so a bigger queue is carried as consecutive runs. They are driven from
 * here — a store outside any page — so the work (and the window that shows it) survives navigating elsewhere. Each run is still
 * an ordinary bulk operation: previewed first, paced by the server, every message re-checked as it is sent.
 */
export type ReplayAllPhase = 'preparing' | 'ready' | 'running' | 'ended' | 'error'

interface Chunk {
  readonly ids: readonly number[]
  readonly preview: BulkPreview
  readonly progress?: BulkProgress
}

export interface ReplayAllState {
  readonly phase: ReplayAllPhase
  readonly minimized: boolean
  readonly scopeLabel: string
  /** How many the cloud itself counts in this scope, when it reports one — can be more than ServiceHub has recorded. */
  readonly cloudCount: number | null
  readonly chunks: readonly Chunk[]
  readonly status: BulkStatus
  readonly endedReason: string | null
  readonly error: string | null
  readonly stopping: boolean
}

let state: ReplayAllState | null = null
let query: DeadLetterQuery | null = null
let cancelled = false
let currentJob: string | null = null
const listeners = new Set<() => void>()

const set = (patch: Partial<ReplayAllState> | null) => {
  state = patch === null ? null : ({ ...state!, ...patch })
  listeners.forEach((l) => l())
}

export const useReplayAll = (): ReplayAllState | null =>
  useSyncExternalStore((cb) => { listeners.add(cb); return () => { listeners.delete(cb) } }, () => state)

/** Everything the chunks add up to, shaped like one bulk run so the same progress view can show it. */
export function aggregate(s: ReplayAllState): BulkProgress {
  const sum = (f: (c: Chunk) => number) => s.chunks.reduce((n, c) => n + f(c), 0)
  const willReplay = sum((c) => c.progress?.willReplay ?? c.preview.willReplay)
  const sent = sum((c) => c.progress?.sent ?? 0)
  const failed = sum((c) => c.progress?.failed ?? 0)
  const unknown = sum((c) => c.progress?.unknown ?? 0)
  const ended = s.phase === 'ended'
  return {
    id: 'replay-all',
    status: ended ? s.status : 'running',
    selected: sum((c) => c.ids.length),
    willReplay,
    sent,
    failed,
    unknown,
    remaining: ended ? 0 : Math.max(0, willReplay - sent - failed - unknown),
    heldBack: sum((c) => c.progress?.heldBack ?? c.preview.heldBackCount),
    sampleOnly: false,
    endedReason: s.endedReason,
    previewedAt: '',
    startedAt: null,
    endedAt: null,
    kind: 'replay',
    problems: s.chunks.flatMap((c) => c.progress?.problems ?? []),
  }
}

/** Opens the window and works out — without sending anything — what replaying everything would do. */
export async function openReplayAll(q: DeadLetterQuery, scopeLabel: string, cloudCount: number | null = null): Promise<void> {
  if (state && (state.phase === 'running' || state.phase === 'ready' || state.phase === 'ended')) return set({ minimized: false })
  query = { ...q, status: 'active', reason: undefined, noReason: undefined, entity: undefined, q: undefined, range: 'all', page: undefined, pageSize: undefined }
  cancelled = false
  state = null
  set({ phase: 'preparing', minimized: false, scopeLabel, cloudCount, chunks: [], status: 'running', endedReason: null, error: null, stopping: false })
  try {
    const ids: number[] = []
    const first = await fetchDeadLetters({ ...query, page: 1, pageSize: 100 })
    ids.push(...first.items.map((m) => m.id))
    const pages = Math.ceil(first.paging.total / 100)
    for (let page = 2; page <= pages; page++) ids.push(...(await fetchDeadLetters({ ...query, page, pageSize: 100 })).items.map((m) => m.id))
    const unique = [...new Set(ids)]
    if (unique.length === 0) return set({ phase: 'error', error: 'Nothing is stuck here right now, so there is nothing to replay.' })
    const chunks: Chunk[] = []
    for (let i = 0; i < unique.length; i += BULK_LIMIT) {
      const part = unique.slice(i, i + BULK_LIMIT)
      chunks.push({ ids: part, preview: await previewBulk(part) })
    }
    set({ phase: 'ready', chunks })
  } catch {
    set({ phase: 'error', error: 'ServiceHub couldn’t work out what replaying everything would do, so nothing was sent.' })
  }
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))

const patchChunk = (i: number, patch: Partial<Chunk>) =>
  set({ chunks: state!.chunks.map((c, n) => (n === i ? { ...c, ...patch } : c)) })

/** Starts the runs, one after another. Closing or minimizing the window does not stop them. */
export async function startReplayAll(client: QueryClient): Promise<void> {
  if (!state || state.phase !== 'ready') return
  set({ phase: 'running' })
  let status: BulkStatus = 'completed'
  let endedReason: string | null = null
  try {
    for (let i = 0; i < state!.chunks.length && !cancelled; i++) {
      // A later run is previewed just before it starts, so a preview made at the beginning can never have gone stale.
      const preview = i === 0 ? state!.chunks[i]!.preview : await previewBulk(state!.chunks[i]!.ids)
      patchChunk(i, { preview })
      if (preview.willReplay === 0) continue
      const job = await startBulk(preview.previewId, false)
      currentJob = job.id
      let progress = job
      while (!isEnded(progress.status)) {
        patchChunk(i, { progress })
        await sleep(1000)
        progress = await fetchBulk(job.id)
      }
      patchChunk(i, { progress })
      currentJob = null
      void client.invalidateQueries({ queryKey: deadLetterKeys.all })
      if (progress.status === 'cancelled') { status = 'cancelled'; break }
      if (progress.status !== 'completed') { status = 'stopped'; endedReason = progress.endedReason; break }
    }
    if (cancelled) status = 'cancelled'
  } catch {
    status = 'stopped'
    endedReason = 'ServiceHub lost contact with the API part-way through. What was already sent is in Replayed; check the queue before trying again.'
  }
  currentJob = null
  set({ phase: 'ended', status, endedReason, stopping: false })
  bulkSelection.finished()
  void client.invalidateQueries({ queryKey: deadLetterKeys.all })
  void client.invalidateQueries({ queryKey: replayKeys.all })
  void client.invalidateQueries({ queryKey: ['rules'] })
  void client.invalidateQueries({ queryKey: ['rule-activity'] })
  const a = aggregate(state!)
  const bad = a.failed + a.unknown
  showNotice({
    tone: status !== 'completed' || bad > 0 ? (a.sent === 0 ? 'bad' : 'warn') : 'good',
    title: status === 'cancelled' ? 'Replay all stopped' : status === 'stopped' ? 'Replay all stopped itself' : bad > 0 ? 'Replay all finished with problems' : 'Replay all finished',
    text: `${a.sent.toLocaleString()} sent back${bad > 0 ? `, ${bad.toLocaleString()} did not go` : ''}${a.heldBack > 0 ? `, ${a.heldBack.toLocaleString()} held back` : ''}. Open the replay-all window for details.`,
  })
}

export function stopReplayAll(): void {
  if (!state || state.phase !== 'running') return
  cancelled = true
  set({ stopping: true })
  if (currentJob) void cancelBulk(currentJob)
}

export const minimizeReplayAll = (): void => { if (state) set({ minimized: true }) }
export const restoreReplayAll = (): void => { if (state) set({ minimized: false }) }

/** Closes the window. While runs are going this only minimizes — they carry on. */
export function closeReplayAll(): void {
  if (!state) return
  if (state.phase === 'running') return set({ minimized: true })
  set(null)
}
