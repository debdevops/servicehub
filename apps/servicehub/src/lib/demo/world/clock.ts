import type { StreamEvent } from '../../eventStream'
import { arrive, autoReplay, settle } from './actions'
import type { World } from './model'
import { emit, getWorld, onWorldEvent, save } from './store'

/** How often the made-up world moves on while someone is looking at it. */
export const TICK_MS = 15_000
const KEPT_CYCLES = 30

function note(w: World, agentId: string, now: number, text: string): void {
  const list = w.activity[agentId] ?? []
  w.activity[agentId] = [{ at: new Date(now).toISOString(), source: 'cycle' as const, kind: 'cycle', text, by: null }, ...list].slice(0, KEPT_CYCLES)
}

/**
 * One step of the demo's time: watch windows close, Auto Replay works through what its rules have earned, and the failure that
 * is still happening sends another message. Everything here follows the story — nothing is random.
 */
export function tick(now: number = Date.now()): void {
  const w = getWorld()
  w.ticks++

  const closed = settle(w, now)
  if (closed > 0) {
    note(w, 'recovery-verification', now, `Closed ${closed} watch window${closed === 1 ? '' : 's'}`)
    emit('ReplayVerified', 'Replay', null)
  }

  const sent = autoReplay(w, now)
  if (sent > 0) note(w, 'auto-replay', now, `Replayed ${sent} message${sent === 1 ? '' : 's'} for rules that have earned it`)

  // The timeouts are still happening: every other step, one more message fails on each cloud's dev namespace.
  if (w.ticks % 2 === 0) {
    let seen = 0
    for (const ns of w.namespaces.filter((n) => n.environment === 'dev')) if (arrive(w, ns.id, now)) seen++
    if (seen > 0) note(w, 'dlq-monitor', now, `Found ${seen} new dead letter${seen === 1 ? '' : 's'}`)
  }

  const at = new Date(now).toISOString()
  w.agents = w.agents.map((a) => (a.isPaused || a.cadenceSeconds > 60 ? a : { ...a, lastRunUtc: at }))
  save()
}

/** Starts the demo's clock and passes its events to `listener`. Returns what stops it. */
export function startDemoClock(listener: (event: StreamEvent) => void): () => void {
  const off = onWorldEvent(listener)
  const timer = setInterval(() => tick(), TICK_MS)
  return () => {
    off()
    clearInterval(timer)
  }
}
