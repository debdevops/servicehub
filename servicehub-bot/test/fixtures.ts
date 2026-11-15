/**
 * Made-up ServiceHub answers in the exact shapes of the running API (captured with GET on 2026-11-15). Names and numbers
 * are invented. Each covers a case the live check could not: an AWS connection that is not watched, a Google one whose
 * read failed, approximate and unreported counts, a read-only connection, a component that offers no replay.
 */
import type { ReadPath, Query, ServiceHubReader } from '../src/servicehub.ts'
import type { ComponentOverview, Connection, Coverage, DeadLetterList, RecoverySummary } from '../src/live.ts'

export const AZ = 'aaaaaaaa-0000-0000-0000-000000000001'
export const AWS = 'bbbbbbbb-0000-0000-0000-000000000002'
export const GCP = 'cccccccc-0000-0000-0000-000000000003'
export const AZ_RO = 'dddddddd-0000-0000-0000-000000000004'

export const namespaces: Connection[] = [
  { id: AZ, name: 'sb-shop-prod', displayName: 'Shop', provider: 'azure', environment: 'prod', isActive: true, lastConnectionTestAt: '2026-11-15T08:00:00Z', lastConnectionTestSucceeded: true },
  { id: AWS, name: 'aws-eu', displayName: null, provider: 'aws', environment: 'dev', isActive: true, lastConnectionTestAt: '2026-11-14T09:30:00Z', lastConnectionTestSucceeded: false },
  { id: GCP, name: 'gcp-proj', displayName: 'Pipeline', provider: 'gcp', environment: 'uat', isActive: false, lastConnectionTestAt: null, lastConnectionTestSucceeded: null },
  { id: AZ_RO, name: 'sb-audit', displayName: 'Audit', provider: 'azure', environment: 'dev', isActive: true, lastConnectionTestAt: '2026-11-15T08:00:00Z', lastConnectionTestSucceeded: true },
]

const base = { environment: 'dev', canSend: true, reachable: true, lastError: null, watched: true, recorded: null, approvals: 0, handOffs: 0, entities: [] }

export const overview: ComponentOverview = {
  components: [
    {
      ...base, namespaceId: AZ, componentKey: 'service-bus', cloud: 'azure', name: 'Shop', componentName: 'Service Bus', environment: 'prod',
      deadLetters: { value: 120, kind: 'exact' }, approvals: 7,
      entities: [
        { name: 'orders', parent: null, deadLetters: { value: 100, kind: 'exact' } },
        { name: 'audit-sub', parent: 'events', deadLetters: { value: 20, kind: 'exact' } },
        { name: 'quiet', parent: null, deadLetters: { value: 0, kind: 'exact' } },
      ],
    },
    {
      ...base, namespaceId: AWS, componentKey: 'sqs', cloud: 'aws', name: 'aws-eu', componentName: 'SQS',
      watched: false, deadLetters: { value: 40, kind: 'approximate' }, recorded: { messages: 100, reachedLimit: true, atUtc: '2026-11-15T10:05:00Z' },
    },
    {
      ...base, namespaceId: AWS, componentKey: 'step-functions', cloud: 'aws', name: 'aws-eu', componentName: 'Step Functions',
      deadLetters: { value: 3, kind: 'exact' },
    },
    {
      ...base, namespaceId: GCP, componentKey: 'pubsub', cloud: 'gcp', name: 'Pipeline', componentName: 'Pub/Sub',
      reachable: false, lastError: 'Permission denied on projects/x', watched: false, deadLetters: { value: null, kind: 'notRead' },
    },
    {
      ...base, namespaceId: AZ_RO, componentKey: 'service-bus', cloud: 'azure', name: 'Audit', componentName: 'Service Bus',
      canSend: false, deadLetters: { value: 2, kind: 'exact' },
    },
  ],
}

export const coverage: Coverage = {
  rows: [
    { componentKey: 'service-bus', cloud: 'azure', cloudName: 'Azure', offersReplay: true },
    { componentKey: 'sqs', cloud: 'aws', cloudName: 'AWS', offersReplay: true },
    { componentKey: 'step-functions', cloud: 'aws', cloudName: 'AWS', offersReplay: false },
    { componentKey: 'pubsub', cloud: 'gcp', cloudName: 'Google Cloud', offersReplay: true },
  ],
}

/** Each list also carries a message whose preview holds a secret, to prove the bot never prints one. */
export const SECRET = 'Password=hunter2-bot-fixture'
const item = { id: 1, gist: { preview: `{"note":"${SECRET}"}` } }

export const deadLetterLists: Record<string, DeadLetterList & { items: unknown[] }> = {
  [AZ]: {
    items: [item], paging: { total: 120 },
    groups: [{ reason: 'MaxDeliveryCountExceeded', count: 90 }, { reason: 'DeserializationError', count: 25 }, { reason: 'Custom', count: 3 }, { reason: 'Other', count: 2 }],
    otherReasons: null,
  },
  [AWS]: { items: [item], paging: { total: 100 }, groups: [{ reason: null, count: 100 }], otherReasons: null },
  [AZ_RO]: { items: [item], paging: { total: 2 }, groups: [{ reason: 'TTLExpiredException', count: 2 }], otherReasons: { count: 5, kinds: 2 } },
}

export const summary: RecoverySummary = {
  window: '7d', total: 30,
  states: [
    { state: 'Observing', count: 4 }, { state: 'Recovered', count: 6 }, { state: 'Returned', count: 2 }, { state: 'Unverified', count: 5 },
    { state: 'ExecutionFailed', count: 1 }, { state: 'Declined', count: 12 }, { state: 'Expired', count: 0 },
  ],
  byProvider: [
    { provider: 'azure', total: 20, states: [{ state: 'Recovered', count: 6 }, { state: 'Returned', count: 2 }, { state: 'Declined', count: 12 }], stayedFixedRate: 0.75 },
    { provider: 'aws', total: 10, states: [{ state: 'Unverified', count: 5 }, { state: 'Observing', count: 4 }, { state: 'ExecutionFailed', count: 1 }], stayedFixedRate: null },
  ],
  stayedFixedRate: 0.75,
}

export interface FakeReader extends ServiceHubReader {
  readonly calls: { path: ReadPath; query: Query }[]
}

/** A reader that answers from the fixtures and records every call. `fail` makes one path (or one namespace's list) throw. */
export function fakeReader(answers: Partial<Record<ReadPath, unknown>> = {}, fail: { path?: ReadPath; namespaceId?: string } = {}): FakeReader {
  const calls: { path: ReadPath; query: Query }[] = []
  const data: Record<ReadPath, unknown> = {
    '/namespaces': namespaces, '/fleet/components': overview, '/fleet/coverage': coverage, '/recovery/summary': summary, '/dead-letters': null,
    ...answers,
  }
  return {
    calls,
    async get<T>(path: ReadPath, query: Query = {}): Promise<T> {
      calls.push({ path, query })
      if (fail.path === path) throw new Error(`fixture: ${path} failed`)
      if (path === '/dead-letters') {
        const id = String(query.namespaceId)
        if (fail.namespaceId === id) throw new Error('fixture: reasons failed')
        return (deadLetterLists[id] ?? { items: [], paging: { total: 0 }, groups: [], otherReasons: null }) as T
      }
      return data[path] as T
    },
  }
}
