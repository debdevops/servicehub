import { vi } from 'vitest'
import type { Agent } from '@/lib/api/agents'
import type { ConnectedComponent, ComponentEntity } from '@/lib/api/components'
import type { CoverageRow } from '@/lib/api/coverage'
import type { Me } from '@/lib/api/identity'
import type { PendingWorkItem } from '@/lib/api/pendingWork'

/** Fixtures shared by the 5.0.0 header and Home tests. */

export const coverageRows: CoverageRow[] = [
  {
    id: 'service-bus.dead-letter', componentKey: 'service-bus', cloud: 'azure', cloudName: 'Azure', name: 'Service Bus', stuckPlace: 'Dead-letter queue', detection: 'store',
    detectionWords: '', movesOnBy: '', status: 'supported', statusWords: 'Supported', note: null, class: 'r1ReplayableProvable', offersReplay: true, offersCloudRedrive: false,
    mayEarnAutonomy: true, inSidebar: true, entityKinds: ['queue'], planUnit: null,
  },
  {
    id: 'storage-queues.poison', componentKey: 'storage-queues', cloud: 'azure', cloudName: 'Azure', name: 'Storage Queues', stuckPlace: 'Poison queue', detection: 'store',
    detectionWords: '', movesOnBy: '', status: 'beingValidated', statusWords: 'Being validated', note: null, class: 'r0Unknown', offersReplay: false, offersCloudRedrive: false,
    mayEarnAutonomy: false, inSidebar: true, entityKinds: ['queue'], planUnit: '4.1',
  },
  {
    id: 'sqs.dead-letter', componentKey: 'sqs', cloud: 'aws', cloudName: 'AWS', name: 'SQS', stuckPlace: 'Dead-letter queue', detection: 'store', detectionWords: '', movesOnBy: '',
    status: 'supported', statusWords: 'Supported', note: 'Ends Unverified', class: 'r2ReplayableUnproven', offersReplay: true, offersCloudRedrive: false, mayEarnAutonomy: false,
    inSidebar: true, entityKinds: ['queue'], planUnit: null,
  },
  {
    id: 'pubsub.dead-letter', componentKey: 'pubsub', cloud: 'gcp', cloudName: 'Google Cloud', name: 'Pub/Sub', stuckPlace: 'Dead-letter topic', detection: 'store', detectionWords: '', movesOnBy: '',
    status: 'supported', statusWords: 'Supported', note: null, class: 'r2ReplayableUnproven', offersReplay: true, offersCloudRedrive: false, mayEarnAutonomy: false,
    inSidebar: true, entityKinds: ['subscription'], planUnit: null,
  },
]

export const entity = (over: Partial<ComponentEntity> = {}): ComponentEntity => ({
  name: 'orders-queue', parent: null, kind: 'queue', deadLetterStore: null, noStoreReason: null, deadLetters: { value: 2, kind: 'exact' }, recorded: null, needsYou: 0,
  observedAtUtc: '2026-10-18T10:00:00Z', ...over,
})

export const component = (over: Partial<ConnectedComponent> = {}): ConnectedComponent => ({
  namespaceId: 'ns-azure', componentKey: 'service-bus', cloud: 'azure', name: 'orders-sb', componentName: 'Service Bus', environment: 'dev', canSend: true, reachable: true,
  lastError: null, watched: true, class: 'r1ReplayableProvable', deadLetters: { value: 12, kind: 'exact' }, recorded: null, needsYou: 1, approvals: 1, handOffs: 0,
  observedAtUtc: '2026-10-18T10:00:00Z', entities: [entity({ needsYou: 1 })], ...over,
})

export const me = (over: Partial<Me> = {}): Me => ({
  ownerId: 'o', authMethod: 'session', actor: { identity: 'maya', kind: 'User' }, effectiveRole: 'Admin', governanceActive: false, ...over,
}) as Me

export const agent = (over: Partial<Agent> = {}): Agent => ({
  id: 'auto-replay', name: 'Auto replay', purpose: '', kind: 'worker', authority: 'acts', canAct: true, cadenceSeconds: 30, notes: null, may: [], mayNot: [], health: 'healthy',
  late: false, isPaused: false, lastRunUtc: null, lastResult: null, lastFailure: null, consecutiveFailures: 0, ...over,
}) as Agent

export const approval = (over: Partial<PendingWorkItem> = {}): PendingWorkItem => ({
  kind: 'approval', id: 'e1', entryId: 'e1', agentId: null, dlqMessageId: 41, namespaceId: 'ns-azure', namespaceName: 'orders-sb', provider: 'azure', environment: 'dev', entity: 'orders-queue',
  deadLetterReason: 'MaxDeliveryCountExceeded', ruleId: 1, ruleName: 'r', reasonCode: 'AUTONOMY_GRANT_INSUFFICIENT', reason: 'Stopped and asked', since: new Date(Date.now() - 3 * 3_600_000).toISOString(),
  componentKey: 'service-bus', type: 'approval', family: 'policyBlocked', ...over,
})

export const handOff = (over: Partial<PendingWorkItem> = {}): PendingWorkItem =>
  approval({ kind: 'unresolved', id: 'e2', entryId: 'e2', dlqMessageId: 42, reasonCode: 'REPLAY_OUTCOME_UNKNOWN', type: 'handOff', family: 'outcomeUnknown', ...over })

export const page = (items: PendingWorkItem[]) => ({ items, total: items.length, byProvider: [], agents: 0 })

export const noopMock = vi.fn()
