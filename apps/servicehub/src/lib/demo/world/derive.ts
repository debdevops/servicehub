import type { DeadLetter } from '../../api/deadLetters'
import type { CloudProvider, EnvironmentKind, Namespace } from '../../api/namespaces'
import type { PendingWorkItem } from '../../api/pendingWork'
import type { LedgerEntry } from '../../api/recovery'
import type { ReplayListItem, ReplayVerification } from '../../api/replay'
import type { Rule } from '../../api/rules'
import type { Signature, SignatureTrust } from '../../api/signatures'
import type { DemoDeadLetter, DemoEntry, DemoRule, World } from './model'
import { capabilities, gistOf, stories } from './seed'

/**
 * Everything the demo shows is COUNTED here from the world's rows — a tile, its list and the ledger agree because they are
 * read from the same place. Nothing in this file is a typed-in number.
 */
export const DAY = 86_400_000
export const L4 = { sample: 10, rate: 0.95 } as const
export const L5 = { sample: 30, rate: 0.99 } as const
/** A rule stops itself when fewer than this share of its last replays stayed fixed. */
export const BREAKER = { sample: 20, floor: 0.5 } as const

export interface Scope {
  readonly provider?: CloudProvider
  readonly namespaceId?: string
  readonly environment?: EnvironmentKind
}

export const lowerProvider = (v: unknown): CloudProvider | undefined => (typeof v === 'string' && ['azure', 'aws', 'gcp'].includes(v.toLowerCase()) ? (v.toLowerCase() as CloudProvider) : undefined)
export const scopeOf = (p: Record<string, unknown>): Scope => ({ provider: lowerProvider(p.provider), namespaceId: typeof p.namespaceId === 'string' ? p.namespaceId : undefined, environment: typeof p.environment === 'string' ? (p.environment.toLowerCase() as EnvironmentKind) : undefined })

export const nsOf = (w: World, id: string): Namespace => w.namespaces.find((n) => n.id === id)!
export const proves = (provider: CloudProvider) => capabilities[provider].canProveDlqAbsence

export function inScope(w: World, namespaceId: string, scope: Scope): boolean {
  const ns = nsOf(w, namespaceId)
  return (!scope.provider || ns.provider === scope.provider) && (!scope.namespaceId || ns.id === scope.namespaceId) && (!scope.environment || ns.environment === scope.environment)
}

/** A copy of `value` without the named fields — for handing a demo row to the API's shape. */
export function without<T extends object, K extends keyof T>(value: T, ...keys: K[]): Omit<T, K> {
  const copy = { ...value } as Record<PropertyKey, unknown>
  for (const key of keys) delete copy[key]
  return copy as Omit<T, K>
}

/** The dead letter as the API returns it: the stored row plus its short reading, without the demo's own fields. */
export function toDeadLetter(w: World, d: DemoDeadLetter): DeadLetter {
  return { ...without(d, 'story', 'signatureHash', 'correlationId'), gist: gistOf(d, nsOf(w, d.namespaceId).provider) }
}

export const activeIn = (w: World, namespaceId: string, entity?: string) => w.deadLetters.filter((d) => d.namespaceId === namespaceId && d.status === 'active' && (!entity || d.entityName === entity))

// ── replays and the ledger ───────────────────────────────────────────────────────────────────────────────────────────

export function verificationOf(e: DemoEntry): ReplayVerification {
  const canConfirm = proves(e.provider)
  switch (e.state) {
    case 'Observing': return { status: 'watching', reasonCode: null, confidence: null, watchUntil: e.windowEndsAt, canConfirm, remedy: null }
    case 'Recovered': return { status: 'verified', reasonCode: null, confidence: null, watchUntil: null, canConfirm, remedy: null }
    case 'Returned': return { status: 'returned', reasonCode: null, confidence: e.confidence, watchUntil: null, canConfirm, remedy: null }
    case 'Unverified': return { status: 'verification_required', reasonCode: 'PROVIDER_CANNOT_VERIFY_ABSENCE', confidence: null, watchUntil: null, canConfirm, remedy: 'This cloud cannot prove a replayed message stayed fixed, so a person checks it.' }
    case 'ExecutionFailed': return { status: 'not_sent', reasonCode: null, confidence: null, watchUntil: null, canConfirm, remedy: null }
    default: return { status: 'unknown', reasonCode: null, confidence: null, watchUntil: null, canConfirm, remedy: null }
  }
}

const numberOf = (e: DemoEntry) => Number(e.id.replace(/\D/g, ''))

export function toReplay(w: World, e: DemoEntry): ReplayListItem {
  const row = w.deadLetters.find((d) => d.id === e.dlqMessageId)
  return {
    id: numberOf(e), dlqMessageId: e.dlqMessageId, namespaceId: e.namespaceId, provider: e.provider, messageId: e.messageId, sourceEntity: e.entityName, targetEntity: e.targetEntity,
    replayedAt: e.begunAt, replayedBy: e.actor.identity, actor: e.actor, outcomeStatus: e.state === 'ExecutionFailed' ? 'rejected' : 'accepted', entryState: e.state,
    observationWindowEndsAt: e.windowEndsAt, markerApplied: true, verification: verificationOf(e), gist: row ? gistOf(row, e.provider) : null,
  }
}

export function toLedger(w: World, e: DemoEntry): LedgerEntry {
  return {
    id: e.id, operationId: e.operationId, begunAt: e.begunAt, kind: e.kind, entityName: e.entityName, targetEntity: e.targetEntity, provider: e.provider, namespaceName: nsOf(w, e.namespaceId).displayName,
    actor: e.actor, state: e.state, confidence: e.confidence, dlqMessageId: e.dlqMessageId, closedAt: e.closedAt, entityType: e.entityName.includes('/subscriptions/') ? 'subscription' : 'queue', messageId: e.messageId, level: e.level,
  }
}

export const replaysOf = (w: World) => w.entries.filter((e) => e.kind === 'Replay')
export const newestFirst = <T extends { begunAt: string }>(items: T[]) => [...items].sort((a, b) => b.begunAt.localeCompare(a.begunAt))
export const withinWindow = (iso: string, window: unknown, now: number) => {
  const ms = { '24h': DAY, today: DAY, '7d': 7 * DAY, '30d': 30 * DAY }[String(window)]
  return ms === undefined || now - Date.parse(iso) <= ms
}

// ── trust ────────────────────────────────────────────────────────────────────────────────────────────────────────────

export interface TrustCount {
  readonly recovered: number
  readonly returned: number
  readonly failed: number
  readonly unverified: number
  readonly sample: number
  readonly rate: number | null
  readonly level: 'approve' | 'standing' | 'unattended'
}

/** What a kind of failure has earned, counted from its verified replays — the same thresholds the real Trust Evaluator uses. */
export function trustOf(w: World, signatureHash: string, provider: CloudProvider): TrustCount {
  const mine = w.entries.filter((e) => e.kind === 'Replay' && e.signatureHash === signatureHash)
  const count = (state: DemoEntry['state']) => mine.filter((e) => e.state === state).length
  const recovered = count('Recovered'), returned = count('Returned'), failed = count('ExecutionFailed'), unverified = count('Unverified')
  const sample = recovered + returned + failed
  const rate = sample === 0 ? null : recovered / sample
  const meets = (t: { sample: number; rate: number }) => proves(provider) && sample >= t.sample && (rate ?? 0) >= t.rate
  return { recovered, returned, failed, unverified, sample, rate, level: meets(L5) ? 'unattended' : meets(L4) ? 'standing' : 'approve' }
}

export function toTrust(w: World, signatureHash: string, provider: CloudProvider): SignatureTrust {
  const t = trustOf(w, signatureHash, provider)
  const canConfirm = proves(provider)
  const target = t.level === 'approve' ? L4 : t.level === 'standing' ? L5 : null
  const reasons: string[] = []
  if (!canConfirm) reasons.push('This cloud cannot prove a replayed message stayed fixed, so nothing here can earn replaying on its own.')
  else if (target && t.sample < target.sample) reasons.push(`It needs at least ${target.sample} verified replays; it has ${t.sample}.`)
  else if (target && (t.rate ?? 0) < target.rate) reasons.push(`Only ${Math.round((t.rate ?? 0) * 100)}% of its replays stayed fixed; it needs ${Math.round(target.rate * 100)}%.`)
  return {
    level: t.level, sampleSize: t.sample, verifiedSuccessRate: t.rate, recovered: t.recovered, returned: t.returned, failed: t.failed, unverified: t.unverified,
    nextLevel: !canConfirm || !target ? null : t.level === 'approve' ? 'standing' : 'unattended',
    moreVerifiedNeeded: !canConfirm || !target ? null : Math.max(0, target.sample - t.sample), rateNeeded: !canConfirm || !target ? null : target.rate,
    cloudCanConfirm: canConfirm, productionCeiling: false, reasons,
  }
}

// ── rules and what waits for a person ────────────────────────────────────────────────────────────────────────────────

/** The dead letters a rule names that are still stuck. A rule names one kind of failure, wherever on that cloud it shows up. */
export function matchesOf(w: World, rule: Pick<DemoRule, 'provider' | 'signatureHash' | 'entityName' | 'reason'>): DemoDeadLetter[] {
  return w.deadLetters.filter((d) => {
    if (d.status !== 'active' || nsOf(w, d.namespaceId).provider !== rule.provider) return false
    if (rule.signatureHash) return d.signatureHash === rule.signatureHash
    return (!rule.entityName || d.entityName === rule.entityName) && (!rule.reason || d.deadLetterReason === rule.reason)
  })
}

/** Whether Auto Replay may act on a rule by itself: the cloud must be able to prove a fix, and the failure must have earned it. */
export function mayActAlone(w: World, rule: DemoRule): boolean {
  return rule.enabled && !!rule.signatureHash && trustOf(w, rule.signatureHash, rule.provider).level !== 'approve'
}

/** The messages a rule is holding back and asking a person about — everything it matches that it may not replay alone. */
export function heldBy(w: World, rule: DemoRule): DemoDeadLetter[] {
  if (!rule.enabled || mayActAlone(w, rule)) return []
  return matchesOf(w, rule).filter((d) => !w.declined.includes(d.id))
}

export function holdReason(rule: Pick<DemoRule, "provider">): { code: string; reason: string } {
  return proves(rule.provider)
    ? { code: 'AUTONOMY_GRANT_INSUFFICIENT', reason: 'The Agent stopped and asked: this kind of failure has not yet earned replaying on its own.' }
    : { code: 'PROVIDER_CANNOT_VERIFY_ABSENCE', reason: `The Agent stopped and asked: ${{ azure: 'Azure', aws: 'AWS', gcp: 'Google Cloud' }[rule.provider]} can’t prove a replayed message stayed fixed, so a person decides.` }
}

export function toRule(w: World, rule: DemoRule): Rule {
  const mine = w.entries.filter((e) => e.ruleId === rule.id && e.kind === 'Replay')
  const held = heldBy(w, rule)
  const stayedFixed = mine.filter((e) => e.state === 'Recovered').length
  const lastReplayed = mine.reduce<string | null>((latest, e) => (latest && latest > e.begunAt ? latest : e.begunAt), null)
  return {
    id: rule.id, name: rule.name, provider: rule.provider, reason: rule.reason, entityName: rule.entityName, signatureHash: rule.signatureHash, maxPerHour: rule.maxPerHour, waitSeconds: rule.waitSeconds, backOff: rule.backOff,
    enabled: rule.enabled, disabledReason: rule.disabledReason, disabledDetail: rule.disabledDetail, updatedAt: rule.updatedAt,
    askedCount: held.length, askedIsLowerBound: false, lastAskedReason: held.length ? holdReason(rule).code : null,
    replayed: mine.length, lastReplayedAt: lastReplayed, verifiedOutcomes: mine.filter((e) => e.state === 'Recovered' || e.state === 'Returned').length, stayedFixed,
    sampleSize: BREAKER.sample, successFloor: BREAKER.floor,
  }
}

/** Everything waiting for a person: replays an agent may not make alone, and rules that stopped themselves. */
export function pendingOf(w: World, scope: Scope & { reason?: string }): PendingWorkItem[] {
  const items: PendingWorkItem[] = []
  const seen = new Set<number>()
  for (const rule of w.rules) {
    const { code, reason } = holdReason(rule)
    for (const d of heldBy(w, rule)) {
      if (seen.has(d.id)) continue // two rules naming the same message still ask once
      seen.add(d.id)
      const ns = nsOf(w, d.namespaceId)
      items.push({
        kind: 'approval', id: `demo-held-${d.id}`, entryId: `demo-held-${d.id}`, agentId: null, dlqMessageId: d.id, namespaceId: ns.id, namespaceName: ns.displayName, provider: ns.provider, environment: ns.environment,
        entity: d.entityName, deadLetterReason: d.deadLetterReason, ruleId: rule.id, ruleName: rule.name, reasonCode: code, reason, since: d.detectedAtUtc,
      })
    }
    if (!rule.enabled && rule.disabledReason === 'CircuitBreaker') {
      const ns = nsOf(w, rule.namespaceId)
      items.push({
        kind: 'rule', id: `demo-rule-${rule.id}`, entryId: null, agentId: null, dlqMessageId: null, namespaceId: ns.id, namespaceName: ns.displayName, provider: ns.provider, environment: ns.environment,
        entity: rule.entityName, deadLetterReason: rule.reason, ruleId: rule.id, ruleName: rule.name, reasonCode: 'CIRCUIT_BREAKER_TRIPPED', reason: rule.disabledDetail ?? 'It stopped itself because its replays were not staying fixed.', since: rule.updatedAt ?? ns.createdAt,
      })
    }
  }
  return items
    .filter((i) => inScope(w, i.namespaceId!, scope) && (!scope.reason || i.deadLetterReason === scope.reason))
    .sort((a, b) => b.since.localeCompare(a.since))
}

// ── failure signatures ───────────────────────────────────────────────────────────────────────────────────────────────

export function signaturesOf(w: World, scope: Scope, days: number, now: number): Signature[] {
  const start = now - days * DAY
  const groups = new Map<string, DemoDeadLetter[]>()
  for (const d of w.deadLetters) {
    if (!inScope(w, d.namespaceId, scope) || Date.parse(d.detectedAtUtc) < start) continue
    const g = groups.get(d.signatureHash)
    if (g) g.push(d)
    else groups.set(d.signatureHash, [d])
  }
  return [...groups].map(([signatureHash, rows]): Signature => {
    const provider = nsOf(w, rows[0].namespaceId).provider
    const def = stories[rows[0].story]
    const daily = Array.from({ length: days }, (_, i) => rows.filter((d) => Math.floor((Date.parse(d.detectedAtUtc) - start) / DAY) === i).length)
    const replays = w.entries.filter((e) => e.kind === 'Replay' && e.signatureHash === signatureHash && inScope(w, e.namespaceId, scope))
    const stayedFixed = replays.filter((e) => e.state === 'Recovered').length
    const returned = replays.filter((e) => e.state === 'Returned').length
    const seen = rows.map((d) => d.detectedAtUtc).sort()
    const recent = daily.slice(-3)
    return {
      signatureHash, provider, reason: rows[0].deadLetterReason ?? 'Unknown', exampleError: rows[0].deadLetterErrorDescription ?? def.carried, entities: [...new Set(rows.map((d) => d.entityName))],
      messages: rows.length, activeNow: rows.filter((d) => d.status === 'active').length, firstSeenAt: seen[0], lastSeenAt: seen[seen.length - 1], daily,
      // Growing = more arrived on each of the last three days than on the day before.
      growing: recent.length === 3 && recent[2] > recent[1] && recent[1] > recent[0],
      replays: { replayed: replays.length, stayedFixed, returned, unverified: replays.filter((e) => e.state === 'Unverified').length },
      replayVerdict: stayedFixed + returned === 0 ? 'unknown' : stayedFixed >= returned ? 'helps' : 'doesnt',
      namespaces: [...new Set(rows.map((d) => d.namespaceId))].map((id) => { const ns = nsOf(w, id); return { id, name: ns.name, displayName: ns.displayName, environment: ns.environment, messages: rows.filter((d) => d.namespaceId === id).length } }),
    }
  })
}
