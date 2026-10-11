import type { ChainVerification, EntryState, LedgerEntryDetail, LedgerEvent, LedgerPage, RecoverySummary, StateCount } from '../../api/recovery'
import { inScope, newestFirst, scopeOf, toLedger, withinWindow } from '../world/derive'
import type { DemoEntry, World } from '../world/model'
import { hashOf } from '../world/random'
import { REAL_WATCH_HOURS, people, stories } from '../world/seed'
import { DemoFile, notFound, page, type Route } from './http'
import { byWhom } from './replay'

const ALL_STATES: readonly EntryState[] = ['Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown', 'Recovered', 'Returned', 'Discarded', 'Unverified', 'WrittenOff', 'Expired', 'Declined']
const system = { identity: 'System:RecoveryVerificationAgent', kind: 'system', label: 'Replay Verifier', isSession: false } as const

/** What the ledger recorded for one entry, in order. Made from the entry, so the list and the detail can never disagree. */
function eventsOf(e: DemoEntry): { eventType: string; at: string; actor: LedgerEvent['actor']; detail: string | null }[] {
  const begun = { at: e.begunAt, actor: e.actor }
  const events = [{ eventType: 'OperationOpened', ...begun, detail: null as string | null }]
  if (e.kind === 'WriteOff') return [...events, { eventType: 'DispositionSet', ...begun, detail: 'Marked handled: a replay cannot fix it.' }]
  events.push({ eventType: 'EntryBegun', ...begun, detail: null }, { eventType: 'ProviderAccepted', ...begun, detail: null })
  if (e.kind === 'Purge') return events
  events.push({ eventType: 'ObservationWindowOpened', ...begun, detail: JSON.stringify({ appliedObservationWindowHours: REAL_WATCH_HOURS }) })
  const closed = { at: e.closedAt ?? e.begunAt, actor: system }
  if (e.state === 'Recovered') events.push({ eventType: 'NoRecurrenceObserved', ...closed, detail: null })
  if (e.state === 'Returned') events.push({ eventType: 'RecurrenceObserved', ...closed, detail: null })
  if (e.state === 'Unverified') events.push({ eventType: 'ObservationUnavailable', ...closed, detail: JSON.stringify({ reason: `${e.provider.toUpperCase()}_NO_ABSENCE_PROOF` }) })
  return events
}

const link = (seq: number) => (seq === 0 ? '0'.repeat(64) : [0, 1, 2, 3].map((i) => hashOf(`demo-chain-${seq}-${i}`)).join(''))

/** Every entry's first sequence number: the chain is one run of events across the whole ledger, in the order they were written. */
function firstSeq(w: World): Map<string, number> {
  const starts = new Map<string, number>()
  let seq = 1
  for (const e of w.entries) {
    starts.set(e.id, seq)
    seq += eventsOf(e).length
  }
  return starts
}

function summary(w: World, p: Record<string, unknown>, now: number): RecoverySummary {
  const scope = scopeOf(p)
  const items = w.entries.filter((e) => inScope(w, e.namespaceId, scope) && withinWindow(e.begunAt, p.window, now))
  const states = (list: DemoEntry[]): StateCount[] => ALL_STATES.map((state) => ({ state, count: list.filter((e) => e.state === state).length }))
  const rate = (list: DemoEntry[]) => {
    const fixed = list.filter((e) => e.state === 'Recovered').length
    const back = list.filter((e) => e.state === 'Returned').length
    return fixed + back === 0 ? null : fixed / (fixed + back)
  }
  const providers = [...new Set(items.map((e) => e.provider))]
  return {
    window: (p.window as RecoverySummary['window']) ?? 'all', total: items.length, states: states(items),
    byProvider: providers.map((provider) => { const mine = items.filter((e) => e.provider === provider); return { provider, total: mine.length, states: states(mine), stayedFixedRate: rate(mine) } }),
    stayedFixedRate: rate(items), returnedConfidence: { exact: items.filter((e) => e.state === 'Returned').length, heuristic: 0 }, replaysAccepted: items.filter((e) => e.kind === 'Replay' && e.state !== 'ExecutionFailed').length,
  }
}

function entriesIn(w: World, p: Record<string, unknown>, now: number): DemoEntry[] {
  const scope = scopeOf(p)
  const q = p.q ? String(p.q).toLowerCase() : null
  return newestFirst(w.entries.filter((e) =>
    inScope(w, e.namespaceId, scope) && withinWindow(e.begunAt, p.window, now) && (!p.state || e.state === p.state) && byWhom(e, p.by)
    && (!p.entity || e.entityName === p.entity) && (!q || `${e.messageId} ${e.entityName} ${e.id}`.toLowerCase().includes(q))))
}

export const recovery: readonly Route[] = [
  ['get', /^\/recovery\/summary$/, (_m, { w, params, now }) => summary(w, params, now)],
  ['get', /^\/recovery\/entries$/, (_m, { w, params, now }): LedgerPage => page(entriesIn(w, params, now).map((e) => toLedger(w, e)), params)],
  ['get', /^\/recovery\/entries\/([^/]+)$/, (m, { w }): LedgerEntryDetail => {
    const e = w.entries.find((x) => x.id === m[1]) ?? notFound('ledger entry')
    const start = firstSeq(w).get(e.id)!
    const events = eventsOf(e).map((x, i): LedgerEvent => ({ seq: start + i, eventType: x.eventType, occurredAt: x.at, actor: x.actor, detail: x.detail, prevHash: link(start + i - 1), entryHash: link(start + i) }))
    return {
      entry: toLedger(w, e), recoveryMarker: e.kind === 'Replay' ? e.id : null, markerApplied: e.kind === 'Replay', deadLetterReason: w.deadLetters.find((d) => d.id === e.dlqMessageId)?.deadLetterReason ?? null,
      verificationResult: e.state === 'Recovered' ? 'NoRecurrence' : e.state === 'Returned' ? 'Recurrence' : e.state === 'Unverified' ? 'Unavailable' : null,
      observationWindowEndsAt: e.windowEndsAt, lastEventSeq: start + events.length - 1, events,
    }
  }],
  ['get', /^\/recovery\/chain$/, (_m, { w }): ChainVerification => ({ ownerId: 'demo', isValid: true, eventsChecked: w.entries.reduce((n, e) => n + eventsOf(e).length, 0), firstDivergentSeq: null, reason: null })],
  ['get', /^\/recovery\/export$/, (_m, { w, params, now }) => {
    const from = typeof params.from === 'string' ? Date.parse(params.from) : 0
    const entries = w.entries.filter((e) => Date.parse(e.begunAt) >= from)
    const evidence = { note: 'ServiceHub demo — made-up data. Nothing here describes a real system.', exportedAt: new Date(now).toISOString(), exportedBy: people.you.label, stories: Object.keys(stories), entries: entries.map((e) => ({ ...toLedger(w, e), events: eventsOf(e) })) }
    return new DemoFile(new Blob([JSON.stringify(evidence, null, 2)], { type: 'application/json' }), 'servicehub-demo-evidence.json')
  }],
]
