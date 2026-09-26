import type { Agent } from '../api/agents'
import type { DeadLetter } from '../api/deadLetters'
import type { Me } from '../api/identity'
import type { CloudProvider, Entity, Namespace, ProviderCapabilities } from '../api/namespaces'
import type { PendingWorkItem } from '../api/pendingWork'
import type { EntryState, LedgerEntry } from '../api/recovery'
import type { ReplayListItem, VerificationStatus } from '../api/replay'

/**
 * The demo's made-up world (unit 6.5). Every name is invented — no real namespace, account or project appears. Each cloud keeps
 * its REAL capability differences (the same values the server's ProviderCapabilities holds): Azure can prove a fix held, AWS and
 * Google cannot, so the demo shows the amber "verification required" state exactly as the product does — never a uniformly green
 * picture that would misrepresent it.
 */
const caps: Readonly<Record<CloudProvider, ProviderCapabilities>> = {
  azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, supportsRecoveryMarker: true, canProveDlqAbsence: true, supportsTopics: true, supportsSubscriptions: true, notes: 'Purge is not supported — the SDK has no reliable single-message delete by sequence number.' },
  aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, supportsRecoveryMarker: true, canProveDlqAbsence: false, supportsTopics: true, supportsSubscriptions: true, notes: 'SQS has no non-destructive peek, so every look is a receive.' },
  gcp: { supportsMessageCounts: false, supportsManualDeadLetter: false, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, supportsRecoveryMarker: true, canProveDlqAbsence: false, supportsTopics: true, supportsSubscriptions: true, notes: 'Pub/Sub has no count API; every pull counts as a delivery attempt.' },
}

const iso = (msAgo: number) => new Date(Date.now() - msAgo).toISOString()
const H = 3_600_000

export const demoNamespaces: readonly Namespace[] = [
  { id: 'demo-azure-0000-0000-000000000001', name: 'contoso-orders', displayName: 'Contoso Orders', description: 'Demo', provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null, isActive: true, createdAt: iso(30 * 24 * H), lastConnectionTestAt: iso(H), lastConnectionTestSucceeded: true, capabilities: caps.azure },
  { id: 'demo-aws-00000-0000-000000000002', name: 'acme-payments', displayName: 'Acme Payments', description: 'Demo', provider: 'aws', environment: 'dev', authType: 'awsAccessKey', awsRegion: 'eu-west-1', gcpProjectId: null, isActive: true, createdAt: iso(20 * 24 * H), lastConnectionTestAt: iso(H), lastConnectionTestSucceeded: true, capabilities: caps.aws },
  { id: 'demo-gcp-00000-0000-000000000003', name: 'globex-events', displayName: 'Globex Events', description: 'Demo', provider: 'gcp', environment: 'uat', authType: 'gcpServiceAccount', awsRegion: null, gcpProjectId: 'globex-demo', isActive: true, createdAt: iso(10 * 24 * H), lastConnectionTestAt: iso(H), lastConnectionTestSucceeded: true, capabilities: caps.gcp },
]

export const demoEntities: Readonly<Record<string, readonly Entity[]>> = {
  [demoNamespaces[0].id]: [
    { name: 'orders', kind: 'queue', activeMessages: 42, deadLetterMessages: 9, deadLetterTargetName: null },
    { name: 'invoices', kind: 'queue', activeMessages: 7, deadLetterMessages: 3, deadLetterTargetName: null },
    { name: 'order-events', kind: 'topic', activeMessages: null, deadLetterMessages: null, deadLetterTargetName: null },
    { name: 'order-events/subscriptions/billing', kind: 'subscription', activeMessages: 3, deadLetterMessages: 2, deadLetterTargetName: null },
  ],
  [demoNamespaces[1].id]: [
    { name: 'payments', kind: 'queue', activeMessages: 118, deadLetterMessages: 6, deadLetterTargetName: 'payments-dlq' },
    { name: 'refunds', kind: 'queue', activeMessages: 4, deadLetterMessages: 2, deadLetterTargetName: 'refunds-dlq' },
  ],
  [demoNamespaces[2].id]: [
    { name: 'clickstream', kind: 'topic', activeMessages: null, deadLetterMessages: null, deadLetterTargetName: null },
    { name: 'clickstream/subscriptions/analytics', kind: 'subscription', activeMessages: null, deadLetterMessages: null, deadLetterTargetName: null },
  ],
}

const reasons = [
  { reason: 'MaxDeliveryCountExceeded', error: 'Unexpected token < in JSON at position 0' },
  { reason: 'ValidationFailed', error: 'missing required field: customerId' },
  { reason: 'DownstreamUnavailable', error: 'LedgerService returned 503 Service Unavailable' },
  { reason: 'TimeoutException', error: 'The operation did not complete within 00:00:30' },
]

let nextId = 1
function build(ns: Namespace, entity: string, count: number, status: DeadLetter['status'] = 'active'): DeadLetter[] {
  return Array.from({ length: count }, (_, i) => {
    const r = ns.provider === 'gcp' ? null : reasons[(i + entity.length) % reasons.length]
    const id = nextId++
    const topic = entity.includes('/subscriptions/') ? entity.split('/')[0] : null
    return {
      id, namespaceId: ns.id, messageId: `${ns.provider}-${entity.replace(/\W/g, '')}-${1000 + id}`, sequenceNumber: 5000 + id, entityName: entity,
      entityType: topic ? 'subscription' : 'queue', topicName: topic, detectedAtUtc: iso((i * 5 + 1) * H), enqueuedTimeUtc: iso((i * 5 + 2) * H),
      deliveryCount: ns.provider === 'gcp' ? 0 : 5 + (i % 5), sizeInBytes: 900 + i * 37, deadLetterReason: r?.reason ?? null, deadLetterErrorDescription: r?.error ?? null, status,
      resolvedAt: status === 'resolved' ? iso(i * H) : null, resolutionCause: status === 'resolved' ? (ns.provider === 'azure' ? 'replayedByServiceHub' : 'vanishedExternally') : null,
    }
  })
}

const [az, aws, gcp] = demoNamespaces
export const demoDeadLetters: readonly DeadLetter[] = [
  ...build(az, 'orders', 9), ...build(az, 'invoices', 3), ...build(az, 'order-events/subscriptions/billing', 2), ...build(az, 'orders', 3, 'resolved'),
  ...build(aws, 'payments', 6), ...build(aws, 'refunds', 2), ...build(aws, 'payments', 2, 'resolved'),
  ...build(gcp, 'clickstream/subscriptions/analytics', 5),
]

export function demoBody(d: DeadLetter): string {
  return JSON.stringify({ orderId: `ORD-${d.id * 7919 % 100000}`, customerId: d.deadLetterReason === 'ValidationFailed' ? null : `CUS-${d.id * 31}`, amount: (d.id * 13.37).toFixed(2), currency: 'EUR' }, null, 2)
}

const actor = { identity: 'session:demo', kind: 'user' as const, label: 'from this browser session', isSession: true }
function replay(id: number, d: DeadLetter, status: VerificationStatus, state: EntryState, hoursAgo: number): ReplayListItem {
  const ns = demoNamespaces.find((n) => n.id === d.namespaceId)!
  return {
    id, dlqMessageId: d.id, namespaceId: d.namespaceId, provider: ns.provider, messageId: d.messageId, sourceEntity: d.entityName, targetEntity: d.topicName ?? d.entityName,
    replayedAt: iso(hoursAgo * H), replayedBy: actor.identity, actor, outcomeStatus: 'accepted', entryState: state, observationWindowEndsAt: iso((hoursAgo - 24) * H), markerApplied: true,
    verification: { status, reasonCode: status === 'verification_required' ? 'PROVIDER_CANNOT_VERIFY_ABSENCE' : null, confidence: status === 'returned' ? 'Exact' : null, watchUntil: status === 'watching' ? iso(-20 * H) : null, canConfirm: ns.capabilities!.canProveDlqAbsence, remedy: status === 'verification_required' ? 'SETUP_DLQ_OBSERVER' : null },
  }
}

const resolved = demoDeadLetters.filter((d) => d.status === 'resolved')
const byNs = (ns: Namespace) => resolved.filter((d) => d.namespaceId === ns.id)
export const demoReplays: readonly ReplayListItem[] = [
  replay(1, byNs(az)[0], 'verified', 'Recovered', 23), replay(2, byNs(az)[1], 'verified', 'Recovered', 22), replay(3, byNs(az)[2], 'returned', 'Returned', 20),
  replay(4, byNs(aws)[0], 'verification_required', 'Unverified', 19), replay(5, byNs(aws)[1], 'verification_required', 'Unverified', 6),
]

export const demoLedger: readonly LedgerEntry[] = demoReplays.map((r, i) => ({
  id: `demo-entry-${i + 1}`, operationId: `demo-op-${i + 1}`, begunAt: r.replayedAt, kind: 'Replay', entityName: r.sourceEntity, targetEntity: r.targetEntity,
  provider: r.provider, namespaceName: demoNamespaces.find((n) => n.id === r.namespaceId)!.displayName, actor: r.actor, state: r.entryState as EntryState,
  confidence: r.verification.confidence, dlqMessageId: r.dlqMessageId, closedAt: r.entryState === 'Observing' ? null : r.observationWindowEndsAt,
}))

export const demoPending: readonly PendingWorkItem[] = demoDeadLetters.filter((d) => d.namespaceId === aws.id && d.status === 'active').slice(0, 2).map((d, i) => ({
  kind: 'approval', id: `demo-pending-${i}`, entryId: `demo-pending-entry-${i}`, agentId: null, dlqMessageId: d.id, namespaceId: aws.id, namespaceName: aws.displayName, provider: 'aws',
  environment: 'dev', entity: d.entityName, deadLetterReason: d.deadLetterReason, ruleId: 1, ruleName: 'Payments timeouts', reasonCode: 'PROVIDER_CANNOT_VERIFY_ABSENCE',
  reason: 'The Agent stopped and asked: AWS can’t prove a replayed message stayed fixed, so a person decides.', since: iso((i + 1) * H),
}))

export const demoAgents: readonly Agent[] = [
  { id: 'dlq-monitor', name: 'Dead-letter Monitor', purpose: 'Looks at every dead-letter queue it can look at without side effects, and records what it finds.', kind: 'watch', authority: 'observes', canAct: false, cadenceSeconds: 60 },
  { id: 'recovery-verification', name: 'Fix Checker', purpose: 'Watches each replayed message for its observation window and records whether it came back.', kind: 'watch', authority: 'observes', canAct: false, cadenceSeconds: 300 },
  { id: 'auto-replay', name: 'Auto Replay', purpose: 'Replays what a rule allows, through the same checks a person gets.', kind: 'act', authority: 'actsWithApproval', canAct: true, cadenceSeconds: 60 },
  { id: 'autonomy-evaluation', name: 'Trust Evaluator', purpose: 'Counts how often replaying each kind of failure really fixed it.', kind: 'decide', authority: 'proposes', canAct: false, cadenceSeconds: 3600 },
].map((a): Agent => ({ ...(a as Pick<Agent, 'id' | 'name' | 'purpose' | 'kind' | 'authority' | 'canAct' | 'cadenceSeconds'>), notes: null, may: [], mayNot: [], health: 'healthy', late: false, isPaused: false, lastRunUtc: iso(0.1 * H), lastResult: { examined: 3, changed: 0, summary: 'nothing to do', degraded: false }, lastFailure: null, consecutiveFailures: 0 }))

export const demoMe: Me = { ownerId: 'demo', authMethod: 'session', actor, effectiveRole: 'Admin', governanceActive: false, grantors: [], recoverRole: 'Admin', namespaceRecoverRoles: {} }
