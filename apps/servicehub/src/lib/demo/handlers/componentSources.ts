import type { CloudProvider } from '../../api/namespaces'
import type { ComponentSource, FailedItem, FailedItemDetail, FailedItems, RedriveCheck } from '../../api/componentSources'
import { notFound, refuse, type Route } from './http'

const MINUTE = 60_000

/** One run the demo's cloud holds: how it ended, how long ago, and what the cloud recorded (read only when someone opens it). */
interface DemoRun { readonly state: string; readonly minutesAgo: number; readonly reason: string | null; readonly cause: string | null }
interface DemoPlace { readonly name: string; readonly runs: readonly DemoRun[] }

/**
 * What each non-message-store component shows in the demo, by the cloud that has it. The real list never carries a run's reason (it is read
 * per run), so the demo's list does not either. Only a connection on the right cloud has a reader, as in the real API.
 */
interface DemoComponent {
  readonly provider: CloudProvider
  readonly kind: string
  readonly place: (name: string) => string
  readonly run: (placeName: string, n: number) => string
  readonly placeLink: (placeName: string) => string
  readonly runLink: (placeName: string, n: number) => string
  readonly places: readonly DemoPlace[]
  /** One line for a row, when the cloud's words are not "State: run-N" (failed records, dead-lettered events). */
  readonly summary?: (run: DemoRun, n: number) => string
  /** Whether the failed work itself is held anywhere; a stream failure only points at the original. */
  readonly copy?: 'none' | 'pointer' | 'held'
}

const REGION = 'eu-west-1'
const arn = (kind: string, rest: string) => `arn:aws:states:${REGION}:123456789012:${kind}:${rest}`
const awsLink = (path: string) => `https://${REGION}.console.aws.amazon.com/states/home?region=${REGION}#${path}`
const gcpName = (rest: string) => `projects/globex-events/locations/europe-west1/workflows/${rest}`
const gcpConsole = (rest: string) => `https://console.cloud.google.com/workflows/workflow/europe-west1/${rest}?project=globex-events`
const SUB = '9d2f4c1e-5b7a-4e3d-8c6f-0a1b2c3d4e5f'
const logicName = (rest: string) => `/subscriptions/${SUB}/resourceGroups/rg-globex/providers/Microsoft.Logic/workflows/${rest}`
const portal = (resourceId: string) => `https://portal.azure.com/#@/resource${resourceId}/logicApp`
const STREAM_IDS: Readonly<Record<string, string>> = { 'orders → process': '11111111-1111-1111-1111-111111111111', 'audit-log → audit': '22222222-2222-2222-2222-222222222222' }
const lambdaConsole = (fn: string) => `https://${REGION}.console.aws.amazon.com/lambda/home?region=${REGION}#/functions/${fn}?tab=configure`
const eventarcName = (rest: string) => `projects/globex-events/locations/europe-west1/triggers/${rest}`
const eventarcConsole = (rest: string) => `https://console.cloud.google.com/eventarc/triggers/europe-west1/${rest}?project=globex-events`

const schedulerPlace = (rest: string) => `projects/globex-events/locations/${rest}`
const SCHEDULER_JOBS = ['nightly-export', 'invoice-sync', 'cache-warm']

const components: Readonly<Record<string, DemoComponent>> = {
  'step-functions': {
    provider: 'aws', kind: 'state machine',
    place: (n) => arn('stateMachine', n), run: (p, i) => arn('execution', `${p}:run-${i}`),
    placeLink: (p) => awsLink(`/statemachines/view/${encodeURIComponent(arn('stateMachine', p))}`),
    runLink: (p, i) => awsLink(`/v2/executions/details/${encodeURIComponent(arn('execution', `${p}:run-${i}`))}`),
    places: [
      { name: 'payment-settlement', runs: [
        { state: 'FAILED', minutesAgo: 14, reason: 'States.TaskFailed', cause: 'The settlement call to the bank gateway returned 502.' },
        { state: 'TIMED_OUT', minutesAgo: 95, reason: 'States.Timeout', cause: 'Waiting for the bank to confirm took longer than 300 seconds.' },
        { state: 'FAILED', minutesAgo: 240, reason: 'States.TaskFailed', cause: 'The settlement call to the bank gateway returned 502.' }] },
      { name: 'refund-flow', runs: [
        { state: 'ABORTED', minutesAgo: 480, reason: null, cause: null },
        { state: 'FAILED', minutesAgo: 1500, reason: 'States.Runtime', cause: 'The input did not have the field $.orderId.' }] },
    ],
  },
  'logic-apps': {
    provider: 'azure', kind: 'logic app',
    place: (n) => logicName(n), run: (p, i) => `${logicName(p)}/runs/0858${i}`,
    placeLink: (p) => portal(logicName(p)),
    runLink: (p) => portal(logicName(p)),
    places: [
      { name: 'order-intake', runs: [
        { state: 'Failed', minutesAgo: 18, reason: 'ActionFailed', cause: "The action 'Send_to_warehouse' failed: 502 Bad Gateway." },
        { state: 'TimedOut', minutesAgo: 205, reason: 'Timeout', cause: 'The run waited longer than its 24 hour limit for an approval.' }] },
      { name: 'invoice-sync', runs: [
        { state: 'Cancelled', minutesAgo: 700, reason: null, cause: null }] },
    ],
  },
  'event-grid': {
    provider: 'azure', kind: 'dead-letter container',
    place: (n) => `blob://stgglobex/${n}`, run: (p, i) => `blob://stgglobex/${p}/orders/2026/10/01/e${i}.json`,
    placeLink: () => 'https://portal.azure.com/#@/resource/subscriptions/9d2f4c1e-5b7a-4e3d-8c6f-0a1b2c3d4e5f/resourceGroups/rg-globex/providers/Microsoft.Storage/storageAccounts/stgglobex/overview',
    runLink: () => '',
    summary: (_r, n) => `Dead-lettered event: e${n}.json`,
    places: [
      { name: 'deadletter', runs: [
        { state: 'DEAD_LETTERED', minutesAgo: 35, reason: 'MaxDeliveryAttemptsExceeded', cause: '3 delivery attempts; last outcome Probation; last HTTP status 500' },
        { state: 'DEAD_LETTERED', minutesAgo: 260, reason: 'MaxDeliveryAttemptsExceeded', cause: '3 delivery attempts; last outcome Probation; last HTTP status 503' },
        { state: 'DEAD_LETTERED', minutesAgo: 1900, reason: 'TTLExpired', cause: '1 delivery attempt; last outcome NotFound; last HTTP status 404' }] },
    ],
  },
  'aws-streams': {
    provider: 'aws', kind: 'stream consumer',
    place: (n) => STREAM_IDS[n] ?? n, run: (p, i) => `${STREAM_IDS[p] ?? p}/msg-${i}`,
    placeLink: () => lambdaConsole('process'),
    runLink: () => lambdaConsole('process'),
    copy: 'pointer',
    summary: () => 'Failed batch of 3 records from shard shardId-000000000001 (RetryAttemptsExhausted)',
    places: [
      { name: 'orders → process', runs: [
        { state: 'FAILED', minutesAgo: 9, reason: 'RetryAttemptsExhausted', cause: 'stream orders; shard shardId-000000000001; sequence 4959 to 4961; 3 records; invoked 3 times; function error Unhandled. The original records may have expired.' },
        { state: 'FAILED', minutesAgo: 120, reason: 'RetryAttemptsExhausted', cause: 'stream orders; shard shardId-000000000001; sequence 4710 to 4712; 3 records; invoked 3 times; function error Unhandled. The original records may have expired.' }] },
    ],
  },
  eventarc: {
    provider: 'gcp', kind: 'trigger',
    place: (n) => eventarcName(n), run: (p, i) => `${eventarcName(p)}#${100000 + i}`,
    placeLink: (p) => eventarcConsole(p),
    runLink: (p) => eventarcConsole(p),
    summary: () => 'Undelivered event: google.cloud.pubsub.topic.v1.messagePublished',
    places: [
      { name: 'orders-to-fulfilment', runs: [
        { state: 'UNDELIVERED', minutesAgo: 41, reason: 'HTTP 503 from the Cloud Run service', cause: 'tried 5 times; event type google.cloud.pubsub.topic.v1.messagePublished; from //pubsub.googleapis.com/projects/globex-events/topics/orders' },
        { state: 'UNDELIVERED', minutesAgo: 380, reason: 'HTTP 503 from the Cloud Run service', cause: 'tried 5 times; event type google.cloud.pubsub.topic.v1.messagePublished; from //pubsub.googleapis.com/projects/globex-events/topics/orders' }] },
    ],
  },
  workflows: {
    provider: 'gcp', kind: 'workflow',
    place: (n) => gcpName(n), run: (p, i) => `${gcpName(p)}/executions/run-${i}`,
    placeLink: (p) => gcpConsole(`${p}/executions`),
    runLink: (p, i) => gcpConsole(`${p}/execution/run-${i}`),
    places: [
      { name: 'order-fulfilment', runs: [
        { state: 'FAILED', minutesAgo: 22, reason: '{"message":"HTTP server responded with error code 503","tags":["HttpError"]}', cause: 'Error in step call_warehouse' },
        { state: 'CANCELLED', minutesAgo: 130, reason: null, cause: null }] },
      { name: 'invoice-export', runs: [
        { state: 'FAILED', minutesAgo: 610, reason: '{"message":"Key not found: customerId","tags":["KeyError"]}', cause: 'Error in step build_row' }] },
    ],
  },
  // A place is a location; each failed item is a job whose last attempt failed (only the last attempt is known, not a history).
  'cloud-scheduler': {
    provider: 'gcp', kind: 'location',
    place: (n) => schedulerPlace(n), run: (p, i) => `${schedulerPlace(p)}/jobs/${SCHEDULER_JOBS[i - 1]}`,
    placeLink: () => 'https://console.cloud.google.com/cloudscheduler?project=globex-events',
    runLink: (p, i) => `https://console.cloud.google.com/cloudscheduler/jobs/edit/${p}/${SCHEDULER_JOBS[i - 1]}?project=globex-events`,
    summary: (r, n) => `Last attempt failed: ${r.reason} (${SCHEDULER_JOBS[n - 1]})`,
    places: [
      { name: 'europe-west1', runs: [
        { state: 'UNAVAILABLE', minutesAgo: 35, reason: 'Unavailable', cause: 'The target did not answer.' },
        { state: 'DEADLINE_EXCEEDED', minutesAgo: 190, reason: 'Deadline exceeded', cause: 'The target took longer than the job’s attempt deadline.' },
        { state: 'PERMISSION_DENIED', minutesAgo: 700, reason: 'Permission denied', cause: 'The job’s service account may not call the target.' }] },
    ],
  },
}

const WORDS: Record<string, string> = {
  FAILED: 'Failed', TIMED_OUT: 'Timed out', ABORTED: 'Aborted', CANCELLED: 'Cancelled', Failed: 'Failed', TimedOut: 'Timed out', Cancelled: 'Cancelled',
}

function items(c: DemoComponent, now: number): FailedItem[] {
  return c.places.flatMap((p) => p.runs.map((r, i) => ({
    id: c.run(p.name, i + 1), place: c.place(p.name), atUtc: new Date(now - r.minutesAgo * MINUTE).toISOString(), status: r.state, reasonCode: null,
    summary: c.summary ? c.summary(r, i + 1) : `${WORDS[r.state] ?? r.state}: run-${i + 1}`, copy: c.copy ?? ('none' as const), consoleUrl: c.runLink(p.name, i + 1),
  }))).sort((a, b) => Date.parse(b.atUtc!) - Date.parse(a.atUtc!))
}

const componentFor = (providerOf: (id: string) => CloudProvider | undefined, id: string, key: string): DemoComponent => {
  const provider = providerOf(id) ?? notFound('namespace')
  const c = components[key]
  return c && c.provider === provider ? c : refuse(404, 'not_found', `No reader is registered for '${key}' on this connection.`)
}

export const componentSources: readonly Route[] = [
  ['get', /^\/namespaces\/([^/]+)\/components\/([^/]+)\/source$/, (m, { w }): ComponentSource => {
    const c = componentFor((id) => w.namespaces.find((n) => n.id === id)?.provider, m[1], m[2])
    return {
      componentKey: m[2], available: true, reason: null, reasonInList: false, placesReachedLimit: false, placesIncomplete: false,
      places: c.places.map((p) => ({ id: c.place(p.name), name: p.name, kind: c.kind, consoleUrl: c.placeLink(p.name) })),
    }
  }],
  ['get', /^\/namespaces\/([^/]+)\/components\/([^/]+)\/failed$/, (m, { w, params, now }): FailedItems => {
    const c = componentFor((id) => w.namespaces.find((n) => n.id === id)?.provider, m[1], m[2])
    const place = typeof params.place === 'string' ? params.place : null
    return { componentKey: m[2], items: items(c, now).filter((i) => place === null || i.place === place), reachedLimit: false, incomplete: false }
  }],
  // The demo's catalogue offers no cloud redrive (no row is validated on a real cloud), so the real gate would refuse. The demo says the same.
  ['get', /^\/namespaces\/([^/]+)\/component-redrives\/([^/]+)\/check$/, (m, { w }): RedriveCheck => {
    componentFor((id) => w.namespaces.find((n) => n.id === id)?.provider, m[1], m[2])
    return { canRedrive: false, reasonCode: 'REDRIVE_NOT_OFFERED', message: 'Redrive is not offered for this component yet. It is handed to a person, who re-runs it in the cloud console.' }
  }],
  ['post', /^\/namespaces\/([^/]+)\/component-redrives\/([^/]+)$/, (m, { w }): never => {
    componentFor((id) => w.namespaces.find((n) => n.id === id)?.provider, m[1], m[2])
    return refuse(403, 'REDRIVE_NOT_OFFERED', 'Redrive is not offered for this component yet. It is handed to a person, who re-runs it in the cloud console.')
  }],
  ['get', /^\/namespaces\/([^/]+)\/components\/([^/]+)\/failed\/detail$/, (m, { w, params, now }): FailedItemDetail => {
    const c = componentFor((id) => w.namespaces.find((n) => n.id === id)?.provider, m[1], m[2])
    const id = typeof params.itemId === 'string' ? params.itemId : refuse(400, 'validation_failed', "'itemId' is required.")
    const item = items(c, now).find((i) => i.id === id)
    if (!item) return { item: null, reason: null, cause: null, startedUtc: null, stoppedUtc: null }
    const place = c.places.find((p) => c.place(p.name) === item.place)!
    const run = place.runs.find((_, n) => c.run(place.name, n + 1) === id)!
    return { item: { ...item, reasonCode: null }, reason: run.reason, cause: run.cause, startedUtc: new Date(Date.parse(item.atUtc!) - 2 * MINUTE).toISOString(), stoppedUtc: item.atUtc }
  }],
]
