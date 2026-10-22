import type { CloudProvider } from '../../api/namespaces'
import type { ComponentSource, FailedItem, FailedItemDetail, FailedItems } from '../../api/componentSources'
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
}

const REGION = 'eu-west-1'
const arn = (kind: string, rest: string) => `arn:aws:states:${REGION}:123456789012:${kind}:${rest}`
const awsLink = (path: string) => `https://${REGION}.console.aws.amazon.com/states/home?region=${REGION}#${path}`
const gcpName = (rest: string) => `projects/globex-events/locations/europe-west1/workflows/${rest}`
const gcpConsole = (rest: string) => `https://console.cloud.google.com/workflows/workflow/europe-west1/${rest}?project=globex-events`

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
}

const WORDS: Record<string, string> = { FAILED: 'Failed', TIMED_OUT: 'Timed out', ABORTED: 'Aborted', CANCELLED: 'Cancelled' }

function items(c: DemoComponent, now: number): FailedItem[] {
  return c.places.flatMap((p) => p.runs.map((r, i) => ({
    id: c.run(p.name, i + 1), place: c.place(p.name), atUtc: new Date(now - r.minutesAgo * MINUTE).toISOString(), status: r.state, reasonCode: null,
    summary: `${WORDS[r.state] ?? r.state}: run-${i + 1}`, copy: 'none' as const, consoleUrl: c.runLink(p.name, i + 1),
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
  ['get', /^\/namespaces\/([^/]+)\/components\/([^/]+)\/failed\/detail$/, (m, { w, params, now }): FailedItemDetail => {
    const c = componentFor((id) => w.namespaces.find((n) => n.id === id)?.provider, m[1], m[2])
    const id = typeof params.itemId === 'string' ? params.itemId : refuse(400, 'validation_failed', "'itemId' is required.")
    const item = items(c, now).find((i) => i.id === id)
    if (!item) return { item: null, reason: null, cause: null, startedUtc: null, stoppedUtc: null }
    const place = c.places.find((p) => c.place(p.name) === item.place)!
    const run = place.runs[Number(id.slice(id.lastIndexOf('run-') + 4)) - 1]!
    return { item: { ...item, reasonCode: null }, reason: run.reason, cause: run.cause, startedUtc: new Date(Date.parse(item.atUtc!) - 2 * MINUTE).toISOString(), stoppedUtc: item.atUtc }
  }],
]
