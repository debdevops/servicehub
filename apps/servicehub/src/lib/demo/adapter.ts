import type { AxiosAdapter, AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { AxiosError } from 'axios'
import type { DeadLetter } from '../api/deadLetters'
import type { CloudProvider } from '../api/namespaces'
import type { EntryState } from '../api/recovery'
import { demoAgents, demoBody, demoDeadLetters, demoEntities, demoLedger, demoMe, demoNamespaces, demoPending, demoReplays } from './fixtures'

const ALL_STATES: readonly EntryState[] = ['Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown', 'Recovered', 'Returned', 'Discarded', 'Unverified', 'WrittenOff', 'Expired', 'Declined']
const lower = (v: unknown) => (typeof v === 'string' ? v.toLowerCase() : undefined) as CloudProvider | undefined
const nsOf = (id: string) => demoNamespaces.find((n) => n.id === id)!

function problem(config: InternalAxiosRequestConfig, status: number, code: string, detail: string): never {
  const response = { data: { code, detail, title: detail, status }, status, statusText: code, headers: {}, config } as AxiosResponse
  throw new AxiosError(detail, String(status), config, undefined, response)
}

function summarise(provider?: CloudProvider) {
  const items = demoLedger.filter((e) => !provider || e.provider === provider)
  const states = (list: typeof items) => ALL_STATES.map((state) => ({ state, count: list.filter((e) => e.state === state).length }))
  const rate = (list: typeof items) => { const fixed = list.filter((e) => e.state === 'Recovered').length, back = list.filter((e) => e.state === 'Returned').length; return fixed + back === 0 ? null : fixed / (fixed + back) }
  const providers = [...new Set(items.map((e) => e.provider!))]
  return { window: '24h', total: items.length, states: states(items), byProvider: providers.map((p) => { const l = items.filter((e) => e.provider === p); return { provider: p, total: l.length, states: states(l), stayedFixedRate: rate(l) } }), stayedFixedRate: rate(items), returnedConfidence: { exact: 1, heuristic: 0 }, replaysAccepted: items.length }
}

function listDeadLetters(p: Record<string, unknown>) {
  const provider = lower(p.provider)
  const status = (p.status as string) ?? 'active'
  let rows: DeadLetter[] = demoDeadLetters.filter((d) => (!provider || nsOf(d.namespaceId).provider === provider) && (!p.namespaceId || d.namespaceId === p.namespaceId) && (status === 'all' || (status === 'resolved' ? d.status !== 'active' : d.status === 'active')))
  if (p.entity) rows = rows.filter((d) => d.entityName === p.entity)
  if (p.q) rows = rows.filter((d) => `${d.messageId} ${d.entityName} ${d.deadLetterReason ?? ''}`.toLowerCase().includes(String(p.q).toLowerCase()))
  const scope = rows
  if (p.noReason) rows = rows.filter((d) => !d.deadLetterReason)
  else if (p.reason) rows = rows.filter((d) => d.deadLetterReason === p.reason)
  const page = Number(p.page ?? 1), size = Number(p.pageSize ?? 25)
  const counts = new Map<string | null, number>()
  scope.forEach((d) => counts.set(d.deadLetterReason, (counts.get(d.deadLetterReason) ?? 0) + 1))
  return {
    items: rows.slice((page - 1) * size, page * size), paging: { total: rows.length, page, pageSize: size },
    groups: [...counts].map(([reason, count]) => ({ reason, count })).sort((a, b) => b.count - a.count), otherReasons: null, entities: [...new Set(scope.map((d) => d.entityName))],
  }
}

type Route = [RegExp, (m: RegExpMatchArray, p: Record<string, unknown>) => unknown]
const routes: Route[] = [
  [/^\/namespaces$/, () => demoNamespaces],
  [/^\/namespaces\/([^/]+)$/, (m) => nsOf(m[1])],
  [/^\/namespaces\/([^/]+)\/entities$/, (m) => ({ namespaceId: m[1], entities: demoEntities[m[1]] ?? [] })],
  [/^\/namespaces\/([^/]+)\/stats$/, (m) => { const e = demoEntities[m[1]] ?? []; const can = nsOf(m[1]).capabilities!.supportsMessageCounts; return { namespaceId: m[1], entities: (['queue', 'topic', 'subscription'] as const).map((kind) => ({ kind, count: e.filter((x) => x.kind === kind).length })), activeMessages: can ? e.reduce((s, x) => s + (x.activeMessages ?? 0), 0) : null, deadLetterMessages: can ? e.reduce((s, x) => s + (x.deadLetterMessages ?? 0), 0) : null, messageCountsSupported: can, observedAt: new Date().toISOString() } }],
  [/^\/namespaces\/([^/]+)\/messages\/peek$/, (m, p) => ({ namespaceId: m[1], entity: p.entity, subscription: p.subscription ?? null, deadLetter: false, messages: Array.from({ length: 12 }, (_, i) => ({ messageId: `live-${i}`, sequenceNumber: 9000 + i, body: `{"orderId":"ORD-${4000 + i}","status":"pending"}`, contentType: 'application/json', correlationId: null, sessionId: null, subject: null, enqueuedTime: new Date(Date.now() - i * 60_000).toISOString(), deliveryCount: 1, deadLetterReason: null, deadLetterErrorDescription: null, applicationProperties: {}, sizeInBytes: 48, isFromDeadLetter: false })), paging: { max: 100, returned: 12, nextFromSequenceNumber: null }, peek: { repeatable: true, warning: null } })],
  [/^\/namespaces\/([^/]+)\/messages\/scheduled$/, (_m, p) => ({ entity: p.entity, subscription: null, capped: false, messages: [{ messageId: 'reminder-1', sequenceNumber: 77, scheduledFor: new Date(Date.now() + 2 * 3_600_000).toISOString(), sizeInBytes: 64, contentType: 'application/json', bodyPreview: '{"remind":"invoice overdue"}' }] })],
  [/^\/dead-letters$/, (_m, p) => listDeadLetters(p)],
  [/^\/dead-letters\/trend$/, (_m, p) => ({ days: Number(p.days ?? 7), series: Array.from({ length: Number(p.days ?? 7) }, (_, i) => ({ date: new Date(Date.now() - (Number(p.days ?? 7) - 1 - i) * 86_400_000).toISOString().slice(0, 10), new: [3, 5, 2, 8, 4, 6, 9][i % 7], resolved: [2, 4, 3, 5, 5, 3, 4][i % 7] })) })],
  [/^\/dead-letters\/(\d+)$/, (m) => { const d = demoDeadLetters.find((x) => x.id === Number(m[1])); return d && { item: d, bodyPreview: demoBody(d), bodyIsPreview: false, contentType: 'application/json', correlationId: `corr-${d.id}`, sessionId: null, applicationPropertiesJson: '{"source":"checkout"}', resolvedAt: d.resolvedAt ?? null, othersLikeIt: demoDeadLetters.filter((x) => x.entityName === d.entityName && x.deadLetterReason === d.deadLetterReason && x.id !== d.id).length } }],
  [/^\/dead-letters\/(\d+)\/replay-proposal$/, (m) => { const d = demoDeadLetters.find((x) => x.id === Number(m[1]))!; const ns = nsOf(d.namespaceId); const proves = ns.capabilities!.canProveDlqAbsence; return { dlqMessageId: d.id, messageId: d.messageId, sourceEntity: d.entityName, targetEntity: d.topicName ?? d.entityName, namespaceName: ns.displayName, provider: ns.provider, environment: ns.environment, stampsRecoveryMarker: true, priorAttempts: 0, attemptCap: 3, othersLikeIt: 2, observationWindowHours: 24, canConfirm: proves, verdict: 'Allow', reasonCode: null, approvable: false, canExecute: d.status === 'active', blockedCode: d.status === 'active' ? null : 'NOT_ACTIVE', checks: [{ id: 'status', label: 'Still in the dead-letter queue', state: 'passed', detail: null }, { id: 'environment', label: 'Not a production namespace', state: 'passed', detail: ns.environment }, { id: 'frequency', label: 'Not replayed too often', state: 'passed', detail: '0 of 3 earlier attempts' }, proves ? { id: 'verification', label: 'The cloud can confirm whether it stayed fixed', state: 'passed', detail: null } : { id: 'verification', label: 'The cloud cannot prove it stayed fixed', state: 'warning', detail: 'The result will read “verification required”, never “verified”.' }] } }],
  [/^\/replays$/, (_m, p) => { const provider = lower(p.provider); const items = demoReplays.filter((r) => (!provider || r.provider === provider) && (p.dlqMessageId === undefined || r.dlqMessageId === Number(p.dlqMessageId)) && (!p.result || r.outcomeStatus === p.result)); return { items, total: items.length, page: 1, pageSize: 25 } }],
  [/^\/recovery\/summary$/, (_m, p) => summarise(lower(p.provider))],
  [/^\/recovery\/entries$/, (_m, p) => { const provider = lower(p.provider); const items = demoLedger.filter((e) => (!provider || e.provider === provider) && (!p.state || e.state === p.state)); return { items, total: items.length, page: 1, pageSize: 25 } }],
  [/^\/recovery\/chain$/, () => ({ ownerId: 'demo', isValid: true, eventsChecked: demoLedger.length * 3, firstDivergentSeq: null, reason: null })],
  [/^\/pending-work$/, (_m, p) => { const provider = lower(p.provider); const items = demoPending.filter((i) => !provider || i.provider === provider); return { items, total: items.length, byProvider: items.length ? [{ provider: 'aws', count: items.length }] : [], agents: 0 } }],
  [/^\/agents$/, () => demoAgents],
  [/^\/agents\/([^/]+)\/activity$/, (m) => ({ agentId: m[1], cyclesSinceUtc: null, items: [] })],
  [/^\/me$/, () => demoMe],
  [/^\/settings\/emergency-stop$/, () => ({ active: false, by: null, at: null, reason: null })],
  [/^\/settings$/, () => ({ notifications: { bellAlwaysOn: true, serverChannel: null, channels: [] }, security: { apiKeysConfigured: 0, credentialsEncryptedAtRest: true, keyFingerprint: 'demo' }, emergencyStop: { active: false, by: null, at: null, reason: null } })],
  [/^\/audit$/, () => ({ items: [], page: 1, pageSize: 10, total: 0 })],
  [/^\/rules$/, () => []],
  [/^\/signatures\/authority$/, (_m, p) => { const provider = lower(p.provider); const aws = !provider || provider === 'aws' || provider === 'gcp'; const az = !provider || provider === 'azure'; return { total: (aws ? 2 : 0) + (az ? 2 : 0), capped: false, unattended: 0, standing: az ? 1 : 0, approve: (aws ? 2 : 0) + (az ? 1 : 0), held: [...(aws ? [{ reason: 'cannot_verify', count: 2 }] : []), ...(az ? [{ reason: 'needs_evidence', count: 1 }] : [])], needs: { sample: 10, rate: 0.95 } } }],
  [/^\/signatures$/, () => ({ items: [], total: 0, page: 1, pageSize: 25, all: 0, growing: 0, replayHelps: 0, replayDoesNotHelp: 0 })],
  [/^\/insights$/, () => ({ current: [], cleared: [], lastLookedAt: null })],
  [/^\/fleet\/overview$/, () => ({ window: '24h', since: new Date(Date.now() - 86_400_000).toISOString(), clouds: demoNamespaces.map((n) => ({ provider: n.provider, namespaceCount: 1, capability: n.capabilities!.canProveDlqAbsence ? 'canConfirm' : 'observerRequired', watched: n.capabilities!.supportsRepeatablePeek, active: demoDeadLetters.filter((d) => d.namespaceId === n.id && d.status === 'active').length, newInWindow: 4, resolvedInWindow: 2 })), namespaces: demoNamespaces.map((n) => ({ id: n.id, name: n.name, displayName: n.displayName, provider: n.provider, environment: n.environment, watched: n.capabilities!.supportsRepeatablePeek, active: demoDeadLetters.filter((d) => d.namespaceId === n.id && d.status === 'active').length, newInWindow: 4, resolvedInWindow: 2, topFailure: { reason: 'MaxDeliveryCountExceeded', count: 3 }, health: n.capabilities!.supportsRepeatablePeek ? 'needsALook' : 'cannotTell' })), topFailures: [{ provider: 'azure', environment: 'dev', reason: 'MaxDeliveryCountExceeded', count: 5 }] })],
]

/**
 * The demo's API (unit 6.5): the same client, answering from the made-up world. Reads are served; anything that would change
 * something is refused in words — a demo never sends. An endpoint the demo does not cover answers with the screen's own error
 * state rather than invented data.
 */
export const demoAdapter: AxiosAdapter = async (config) => {
  const url = (config.url ?? '').split('?')[0]
  const method = (config.method ?? 'get').toLowerCase()
  if (method !== 'get') problem(config, 409, 'demo_read_only', 'This is a demo — nothing is sent. Connect your own cloud to do this for real.')
  const params = (config.params ?? {}) as Record<string, unknown>
  for (const [pattern, handler] of routes) {
    const m = url.match(pattern)
    if (!m) continue
    const data = handler(m, params)
    if (data === undefined) problem(config, 404, 'not_found', 'That is not part of the demo.')
    return { data, status: 200, statusText: 'OK', headers: {}, config }
  }
  return problem(config, 404, 'demo_not_covered', 'This part of ServiceHub is not in the demo.')
}
