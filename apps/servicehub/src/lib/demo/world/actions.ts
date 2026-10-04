import type { ReplayActor, ReplayOutcome } from '../../api/replay'
import type { DemoDeadLetter, DemoEntry, DemoRule, World } from './model'
import { BREAKER, mayActAlone, matchesOf, nsOf, proves, trustOf } from './derive'
import { rng } from './random'
import { DEMO_WATCH_MS, REAL_WATCH_HOURS, people, signatureOf, stories } from './seed'
import { emit, save } from './store'

/**
 * Everything that changes the made-up world. Each action does what the real product would record — a ledger entry, an audit
 * row, the dead letter leaving the queue — and nothing else: no request is ever made.
 */
const HOUR = 3_600_000

export function audit(w: World, actor: ReplayActor, action: string, namespaceId: string | null, resource: string | null, now: number, outcome: 'Success' | 'Failure' = 'Success'): void {
  const ns = namespaceId ? nsOf(w, namespaceId) : null
  w.audit.unshift({
    id: `demo-audit-${w.next.audit++}`, timestamp: new Date(now).toISOString(), actor, action, outcome, namespaceId: ns?.id ?? null, namespaceName: ns?.displayName ?? null,
    cloudProvider: ns ? { azure: 'Azure', aws: 'Aws', gcp: 'Gcp' }[ns.provider] : null, environment: ns?.environment ?? null, resourceName: resource, errorDetails: null, correlationId: null,
  })
}

function entry(w: World, row: DemoDeadLetter, kind: DemoEntry['kind'], actor: ReplayActor, now: number, o: { ruleId?: number | null; level?: DemoEntry['level'] } = {}): DemoEntry {
  const n = w.next.entry++
  const provider = nsOf(w, row.namespaceId).provider
  const made: DemoEntry = {
    id: `demo-entry-${String(n).padStart(4, '0')}`, operationId: `demo-op-${String(n).padStart(4, '0')}`, kind, dlqMessageId: row.id, namespaceId: row.namespaceId, provider,
    entityName: row.entityName, targetEntity: row.topicName ?? row.entityName, messageId: row.messageId, signatureHash: row.signatureHash, story: row.story,
    begunAt: new Date(now).toISOString(), actor, ruleId: o.ruleId ?? null, level: kind === 'Replay' ? (o.level ?? 'approve') : null,
    state: kind === 'Replay' ? 'Observing' : kind === 'Purge' ? 'Discarded' : 'WrittenOff', confidence: null,
    windowEndsAt: kind === 'Replay' ? new Date(now + REAL_WATCH_HOURS * HOUR).toISOString() : null,
    closedAt: kind === 'Replay' ? null : new Date(now).toISOString(), dueAt: kind === 'Replay' ? now + DEMO_WATCH_MS : null,
  }
  w.entries.push(made)
  row.status = 'resolved'
  row.resolvedAt = made.begunAt
  row.resolutionCause = kind === 'Replay' ? 'replayedByServiceHub' : kind === 'Purge' ? 'purgedByServiceHub' : 'declaredByOperator'
  return made
}

const outcome = (e: DemoEntry, message: string): ReplayOutcome => ({ entryId: e.id, operationId: e.operationId, result: 'accepted', state: e.state, markerApplied: true, observationWindowEndsAt: e.windowEndsAt, message, errorCode: null })

/** Replays one dead letter. It leaves the queue and is watched; what happens next is decided by its story when the window closes. */
export function replay(w: World, row: DemoDeadLetter, actor: ReplayActor, now: number, o: { ruleId?: number | null; level?: DemoEntry['level'] } = {}): ReplayOutcome {
  const made = entry(w, row, 'Replay', actor, now, o)
  audit(w, actor, 'Replay.Message', row.namespaceId, row.entityName, now)
  save()
  emit('ReplayCompleted', 'Replay', row.namespaceId)
  return outcome(made, 'Demo — nothing was sent. The replay is recorded, and its watch window has opened.')
}

export function purge(w: World, row: DemoDeadLetter, actor: ReplayActor, now: number): ReplayOutcome {
  const made = entry(w, row, 'Purge', actor, now)
  audit(w, actor, 'Purge.Message', row.namespaceId, row.entityName, now)
  save()
  emit('MessagePurged', 'Replay', row.namespaceId)
  return outcome(made, 'Demo — nothing was deleted. The purge is recorded in the ledger.')
}

/**
 * Closes every watch window that has run out. Called before any read, so the demo is right even if no timer ever fired.
 * A cloud that can prove a fix says what happened; one that cannot says so. The story decides, never chance.
 */
export function settle(w: World, now: number): number {
  let closed = 0
  for (const e of w.entries) {
    if (e.state !== 'Observing' || e.dueAt === null || e.dueAt > now) continue
    const row = w.deadLetters.find((d) => d.id === e.dlqMessageId)
    const fixed = stories[e.story].curable
    e.dueAt = null
    e.closedAt = new Date(now).toISOString()
    if (!proves(e.provider)) {
      e.state = 'Unverified'
    } else if (fixed) {
      e.state = 'Recovered'
    } else {
      e.state = 'Returned'
      e.confidence = 'Exact'
    }
    // A message whose cause was never fixed is back in the queue, whatever the cloud can prove.
    if (row && !fixed) {
      row.status = 'active'
      row.resolvedAt = null
      row.resolutionCause = null
      row.deliveryCount += 1
    }
    closed++
  }
  if (closed === 0) return 0
  tripBreakers(w, now)
  save()
  return closed
}

/** A rule whose replays keep coming back stops itself — the real product's circuit breaker. */
function tripBreakers(w: World, now: number): void {
  for (const rule of w.rules) {
    if (!rule.enabled) continue
    const verified = w.entries.filter((e) => e.ruleId === rule.id && (e.state === 'Recovered' || e.state === 'Returned')).slice(-BREAKER.sample)
    if (verified.length < BREAKER.sample) continue
    const stayed = verified.filter((e) => e.state === 'Recovered').length
    if (stayed / verified.length >= BREAKER.floor) continue
    rule.enabled = false
    rule.disabledReason = 'CircuitBreaker'
    rule.disabledDetail = `Only ${stayed} of its last ${verified.length} replays stayed fixed, so it stopped itself.`
    rule.updatedAt = new Date(now).toISOString()
  }
}

/** One cycle of the Auto Replay agent: for each rule that may act alone, replay one waiting message. */
export function autoReplay(w: World, now: number): number {
  if (w.emergencyStop.active || w.agents.find((a) => a.id === 'auto-replay')?.isPaused) return 0
  let sent = 0
  for (const rule of w.rules) {
    if (!mayActAlone(w, rule)) continue
    const row = matchesOf(w, rule)[0]
    if (!row) continue
    replay(w, row, people.auto, now, { ruleId: rule.id, level: trustOf(w, rule.signatureHash!, rule.provider).level })
    sent++
  }
  return sent
}

/** A new dead letter of the story that is still happening. On a watched cloud it is seen at once; elsewhere it waits for a look. */
export function arrive(w: World, namespaceId: string, now: number): boolean {
  const ns = nsOf(w, namespaceId)
  const template = w.deadLetters.find((d) => d.namespaceId === namespaceId && d.story === 'timeouts')
  if (!template) return false
  if (!ns.capabilities?.supportsRepeatablePeek) {
    w.unseen[namespaceId] = (w.unseen[namespaceId] ?? 0) + 1
    save()
    return false
  }
  addTimeout(w, template, now)
  save()
  emit('DlqMessageDetected', 'DeadLetter', namespaceId)
  return true
}

function addTimeout(w: World, template: DemoDeadLetter, now: number): void {
  const r = rng(w.rng)
  const provider = nsOf(w, template.namespaceId).provider
  const at = new Date(now - r.int(20, 50) * 1000).toISOString()
  w.deadLetters.push({
    ...template, id: w.next.deadLetter++, messageId: provider === 'gcp' ? String(11_000_000_000_000 + r.int(0, 899_999_999_999)) : `${r.hex(8)}-${r.hex(4)}-4${r.hex(3)}-a${r.hex(3)}-${r.hex(12)}`,
    sequenceNumber: template.sequenceNumber + w.next.deadLetter, detectedAtUtc: new Date(now).toISOString(), enqueuedTimeUtc: at, status: 'active', resolvedAt: null, resolutionCause: null,
    correlationId: `corr-${r.hex(10)}`, signatureHash: signatureOf(provider, 'timeouts', template.entityName),
  })
  w.rng = r.state
}

/** A person looks at a cloud ServiceHub does not watch: whatever arrived since the last look is seen now. */
export function look(w: World, namespaceId: string, actor: ReplayActor, now: number): { newMessages: number; queues: number } {
  const waiting = w.unseen[namespaceId] ?? 0
  const template = w.deadLetters.find((d) => d.namespaceId === namespaceId && d.story === 'timeouts')
  if (template) for (let i = 0; i < waiting; i++) addTimeout(w, template, now)
  w.unseen[namespaceId] = 0
  audit(w, actor, 'DeadLetters.Look', namespaceId, null, now)
  save()
  emit('DlqMessageDetected', 'DeadLetter', namespaceId)
  return { newMessages: template ? waiting : 0, queues: w.entities[namespaceId]?.filter((e) => e.kind !== 'topic').length ?? 0 }
}

export function ruleById(w: World, id: number): DemoRule | undefined {
  return w.rules.find((r) => r.id === id)
}
