import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as sourcesApi from '@/lib/api/componentSources'
import * as componentsApi from '@/lib/api/components'
import * as enablementApi from '@/lib/api/componentEnablement'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, coverageRows, me } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn(), fetchComponent: vi.fn() }))
vi.mock('@/lib/api/componentEnablement', async (o) => ({ ...(await o<typeof enablementApi>()), fetchEnabledComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), fetchNamespace: vi.fn() }))
vi.mock('@/lib/api/componentSources', async (o) => ({ ...(await o<typeof sourcesApi>()), fetchComponentSource: vi.fn(), fetchFailedItems: vi.fn(), fetchFailedItem: vi.fn() }))

const base: coverageApi.CoverageRow = {
  id: 'x', componentKey: 'x', cloud: 'azure', cloudName: 'Azure', name: 'X', stuckPlace: 'Failed runs', detection: 'runs',
  detectionWords: '', movesOnBy: 'Handed to a person', status: 'beingValidated', statusWords: 'Being validated', note: 'Read only', class: 'r0Unknown', offersReplay: false,
  offersCloudRedrive: false, mayEarnAutonomy: false, inSidebar: true, entityKinds: [], planUnit: 'PP4',
}
const eventGrid: coverageApi.CoverageRow = { ...base, id: 'event-grid.dead-letter-blobs', componentKey: 'event-grid', name: 'Event Grid', stuckPlace: 'Dead-letter blobs', detection: 'records' }
const logicApps: coverageApi.CoverageRow = { ...base, id: 'logic-apps.failed-runs', componentKey: 'logic-apps', name: 'Logic Apps', stuckPlace: 'Failed runs', detection: 'runs' }
const streams: coverageApi.CoverageRow = { ...base, id: 'aws-streams.failure-records', componentKey: 'aws-streams', cloud: 'aws', cloudName: 'AWS', name: 'Kinesis, DynamoDB streams', stuckPlace: 'Failure records point at the original', detection: 'records' }
const eventarc: coverageApi.CoverageRow = { ...base, id: 'eventarc.undelivered', componentKey: 'eventarc', cloud: 'gcp', cloudName: 'Google Cloud', name: 'Eventarc', stuckPlace: 'Undelivered events', detection: 'records' }

const azure = { id: 'ns-3', name: 'globex-entra', displayName: 'Globex Azure', provider: 'azure', environment: 'dev', authType: 'defaultAzureCredential', awsRegion: null, gcpProjectId: null, isActive: true, capabilities: null } as namespacesApi.Namespace
const aws = { id: 'ns-1', name: 'acme', displayName: 'Acme Payments', provider: 'aws', environment: 'dev', authType: 'awsIamRole', awsRegion: 'eu-west-1', gcpProjectId: null, isActive: true, capabilities: null } as namespacesApi.Namespace
const gcp = { id: 'ns-2', name: 'globex', displayName: 'Globex Events', provider: 'gcp', environment: 'dev', authType: 'gcpWorkloadIdentity', awsRegion: null, gcpProjectId: 'globex-1', isActive: true, capabilities: null } as namespacesApi.Namespace
const byId: Record<string, namespacesApi.Namespace> = { 'ns-1': aws, 'ns-2': gcp, 'ns-3': azure }

const item = (n: number, over: Partial<sourcesApi.FailedItem> = {}): sourcesApi.FailedItem => ({
  id: `blob://stgglobex/deadletter/e${n}.json`, place: 'blob://stgglobex/deadletter', atUtc: `2026-10-0${n}T10:00:00Z`, status: 'DEAD_LETTERED', reasonCode: null,
  summary: `Dead-lettered event: e${n}.json`, copy: 'none', consoleUrl: null, ...over,
})
const readable = (key: string, kind: string): sourcesApi.ComponentSource => ({
  componentKey: key, available: true, reason: null, reasonInList: false, placesReachedLimit: false, placesIncomplete: false, places: [{ id: 'blob://stgglobex/deadletter', name: 'deadletter (stgglobex)', kind, consoleUrl: null }],
})

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('components that keep failed records, not runs (Event Grid, Eventarc, stream failures, Logic Apps)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: [...coverageRows, eventGrid, logicApps, streams, eventarc] })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: '2026-10-23T10:00:00Z' })
    vi.mocked(enablementApi.fetchEnabledComponents).mockResolvedValue([
      { namespaceId: 'ns-3', cloud: 'azure', componentKey: 'event-grid' }, { namespaceId: 'ns-3', cloud: 'azure', componentKey: 'logic-apps' },
      { namespaceId: 'ns-1', cloud: 'aws', componentKey: 'aws-streams' }, { namespaceId: 'ns-2', cloud: 'gcp', componentKey: 'eventarc' },
    ])
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([aws, gcp, azure])
    vi.mocked(namespacesApi.fetchNamespace).mockImplementation(async (id) => byId[id] ?? aws)
    vi.mocked(sourcesApi.fetchComponentSource).mockImplementation(async (_id, key) => readable(key, 'dead-letter container'))
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'event-grid', items: [item(2), item(1)], reachedLimit: false, incomplete: false })
  })

  it('is in the menu where it is ticked, with its own page and an Azure connection to open', async () => {
    open('/next/azure/c/event-grid')
    expect(await screen.findByRole('heading', { name: /Event Grid connections \(1\)/ })).toBeInTheDocument()
    const menu = screen.getByRole('navigation', { name: 'ServiceHub' })
    expect(within(menu).getByRole('link', { name: /Event Grid/ })).toHaveAttribute('href', '/next/azure/c/event-grid')
    expect(within(menu).getByRole('link', { name: /Logic Apps/ })).toHaveAttribute('href', '/next/azure/c/logic-apps')
    expect(screen.getByRole('link', { name: 'View Globex Azure' })).toHaveAttribute('href', '/next/azure/c/event-grid/ns-3')
  })

  it('is not in the menu where nobody ticked it', async () => {
    vi.mocked(enablementApi.fetchEnabledComponents).mockResolvedValue([])
    open('/next/azure')
    const menu = await screen.findByRole('navigation', { name: 'ServiceHub' })
    expect(within(menu).queryByRole('link', { name: /Event Grid/ })).not.toBeInTheDocument()
    expect(within(menu).queryByRole('link', { name: /Logic Apps/ })).not.toBeInTheDocument()
  })

  it('says records, not runs: the heading, the table and the cloud’s own status word', async () => {
    open('/next/azure/c/event-grid/ns-3')

    expect(await screen.findByRole('heading', { name: /Failed records \(2\)/ })).toBeInTheDocument()
    const table = screen.getByRole('table', { name: /Failed Event Grid records, newest first/ })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent('Dead-lettered event: e2.json')
    expect(within(rows[0]).getByText('Dead-lettered', { selector: 'span' })).toBeInTheDocument()
    expect(within(screen.getByRole('navigation', { name: 'Sections of this connection' })).getByRole('link', { name: 'Dead-letter blobs' })).toHaveAttribute('aria-current', 'page')
  })

  it('never calls anything a run, and promises no re-run', async () => {
    open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('table')

    const section = screen.getByRole('table').closest('section')!
    expect(within(section).queryByText(/Failed runs/)).not.toBeInTheDocument()
    expect(within(section).queryByRole('button', { name: /replay|re-run|rerun|redrive|purge|retry|delete|start|resubmit/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/To run one again/)).not.toBeInTheDocument()
  })

  it('says plainly that ServiceHub cannot retry Event Grid events, and why, instead of implying a retry is near', async () => {
    open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('table')

    expect(screen.getByText(/ServiceHub cannot retry these: Event Grid has no way to redeliver an event/)).toBeInTheDocument()
    expect(screen.getByText(/send it to every subscription|to every subscription, including ones that already handled it/)).toBeInTheDocument()
    expect(screen.queryByText(/does not move work on/)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Retry/ })).not.toBeInTheDocument()
  })

  it('links an Event Grid blob to its storage account in the portal when the source knows it', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'event-grid', items: [item(1, { consoleUrl: 'https://portal.azure.com/#@/resource/subscriptions/s/overview' })], reachedLimit: false, incomplete: false })
    open('/next/azure/c/event-grid/ns-3')

    const link = await screen.findByRole('link', { name: 'Open Dead-lettered event: e1.json in the cloud console' })
    expect(link).toHaveAttribute('href', 'https://portal.azure.com/#@/resource/subscriptions/s/overview')
  })

  it('offers an undelivered Eventarc event a Retry in Pub/Sub link to the dead letters of its own subscription', async () => {
    vi.mocked(sourcesApi.fetchComponentSource).mockImplementation(async (_id, key) => ({ ...readable(key, 'trigger'), places: [{ id: 'trg', name: 'orders', kind: 'trigger', consoleUrl: null }] }))
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({
      componentKey: 'eventarc', reachedLimit: false, incomplete: false,
      items: [item(1, { id: 'trg#1', place: 'trg', status: 'UNDELIVERED', summary: 'Undelivered event: orders.created', heldIn: { componentKey: 'pubsub', entity: 'eventarc-orders-sub' } })],
    })
    open('/next/gcp/c/eventarc/ns-2')

    const link = await screen.findByRole('link', { name: 'Retry Undelivered event: orders.created in Pub/Sub' })
    expect(link.textContent).toBe('Retry in Pub/Sub…')
    expect(link).toHaveAttribute('href', '/next/gcp/c/pubsub/ns-2?tab=dead-letters&q=eventarc-orders-sub')
    expect(screen.getByText(/use Retry in Pub\/Sub: its dead-letter topic is a Pub\/Sub topic/)).toBeInTheDocument()
    expect(screen.getByText(/press Look now there/)).toBeInTheDocument()
  })

  it('offers no retry link for an event that says it is held nowhere else, and never for a stream failure', async () => {
    open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('table')
    expect(screen.queryByRole('link', { name: /Retry/ })).not.toBeInTheDocument()
  })

  it('does not draw the link while the component it points at is unknown to the catalogue', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({
      componentKey: 'eventarc', reachedLimit: false, incomplete: false,
      items: [item(1, { id: 'trg#1', place: 'trg', status: 'UNDELIVERED', summary: 'Undelivered event: x', heldIn: { componentKey: 'no-such-component', entity: 'e' } })],
    })
    open('/next/gcp/c/eventarc/ns-2')
    await screen.findByRole('table')
    expect(screen.queryByRole('link', { name: /Retry/ })).not.toBeInTheDocument()
  })

  it('says a stream failure only points at the original, which may have expired', async () => {
    vi.mocked(sourcesApi.fetchComponentSource).mockImplementation(async (_id, key) => ({ ...readable(key, 'stream consumer'), places: [{ id: 'u1', name: 'orders → process', kind: 'stream consumer', consoleUrl: null }] }))
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({
      componentKey: 'aws-streams', reachedLimit: false, incomplete: false,
      items: [item(1, { id: 'u1/msg-1', place: 'u1', status: 'FAILED', copy: 'pointer', summary: 'Failed batch of 3 records from shard shardId-1 (RetryAttemptsExhausted)', reasonCode: 'RetryAttemptsExhausted' })],
    })
    open('/next/aws/c/aws-streams/ns-1')

    expect(await screen.findByText(/Failed batch of 3 records/)).toBeInTheDocument()
    expect(screen.getByText('Points at the original, which may have expired.')).toBeInTheDocument()
  })

  it('does not say it for an item that is not a pointer', async () => {
    open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('table')
    expect(screen.queryByText(/Points at the original/)).not.toBeInTheDocument()
  })

  it('opens a record on demand and labels its two times earliest and latest', async () => {
    vi.mocked(sourcesApi.fetchFailedItem).mockResolvedValue({
      item: item(2), reason: 'MaxDeliveryAttemptsExceeded', cause: '3 delivery attempts; last outcome Probation; last HTTP status 500', startedUtc: '2026-10-02T09:00:00Z', stoppedUtc: '2026-10-02T10:00:00Z',
    })
    open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('table')
    expect(sourcesApi.fetchFailedItem).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Show details of Dead-lettered event: e2.json' }))

    expect(await screen.findByText('MaxDeliveryAttemptsExceeded')).toBeInTheDocument()
    expect(screen.getByText('3 delivery attempts; last outcome Probation; last HTTP status 500')).toBeInTheDocument()
    expect(screen.getByText('Earliest')).toBeInTheDocument()
    expect(screen.getByText('Latest')).toBeInTheDocument()
    expect(screen.queryByText('Started')).not.toBeInTheDocument()
    expect(sourcesApi.fetchFailedItem).toHaveBeenCalledTimes(1)
  })

  it('says the record is gone, in its own words', async () => {
    vi.mocked(sourcesApi.fetchFailedItem).mockResolvedValue({ item: null, reason: null, cause: null, startedUtc: null, stoppedUtc: null })
    open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('table')

    await userEvent.click(screen.getByRole('button', { name: 'Show details of Dead-lettered event: e2.json' }))

    expect(await screen.findByText('The cloud no longer has this record.')).toBeInTheDocument()
  })

  it('says there are no records without sounding like a clean bill of health when the read was partial', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'event-grid', items: [], reachedLimit: false, incomplete: true })
    open('/next/azure/c/event-grid/ns-3')

    expect(await screen.findByText(/No failed or undelivered Event Grid records were found in what could be read/)).toBeInTheDocument()
    expect(screen.getByText(/records that exist may be missing/)).toBeInTheDocument()
  })

  it('shows the reason the cloud gave when it keeps nothing, as an error, never as an empty list', async () => {
    vi.mocked(sourcesApi.fetchComponentSource).mockImplementation(async (_id, key) => ({ ...readable(key, 'trigger'), places: [] }))
    vi.mocked(sourcesApi.fetchFailedItems).mockRejectedValue(Object.assign(new Error('x'), {
      isAxiosError: true, response: { status: 400, data: { detail: 'This trigger keeps nothing: its Pub/Sub subscription has no dead-letter policy.' } },
    }))
    open('/next/gcp/c/eventarc/ns-2')

    expect(await screen.findByRole('alert')).toHaveTextContent(/keeps nothing/)
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('an Azure connection that signs in with a connection string is told it needs Entra, and nothing is read', async () => {
    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue({
      ...readable('logic-apps', 'logic app'), available: false, places: [],
      reason: 'Not enabled for this connection: it signs in with a Service Bus connection string or a stored secret. Reading this needs an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential).',
    })
    open('/next/azure/c/logic-apps/ns-3')

    expect(await screen.findByRole('heading', { name: 'Not enabled' })).toBeInTheDocument()
    expect(screen.getByText(/signs in with Entra/)).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(sourcesApi.fetchFailedItems).not.toHaveBeenCalled()
  })

  it('keeps Logic Apps, a run history on Azure, on the run words', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({
      componentKey: 'logic-apps', reachedLimit: false, incomplete: false,
      items: [item(2, { id: '/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/runs/r2', status: 'TimedOut', summary: 'Timed out: r2' }), item(1, { status: 'Cancelled', summary: 'Cancelled: r1' })],
    })
    open('/next/azure/c/logic-apps/ns-3')

    const table = await screen.findByRole('table', { name: /Failed Logic Apps runs, newest first/ })
    expect(within(table).getByText('Timed out', { selector: 'span' })).toBeInTheDocument()
    expect(within(table).getByText('Cancelled', { selector: 'span' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Failed runs \(2\)/ })).toBeInTheDocument()
  })

  it('serves Eventarc on Google Cloud with the same page', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({
      componentKey: 'eventarc', reachedLimit: false, incomplete: false,
      items: [item(1, { status: 'UNDELIVERED', summary: 'Undelivered event: google.cloud.pubsub.topic.v1.messagePublished' })],
    })
    open('/next/gcp/c/eventarc/ns-2')

    const table = await screen.findByRole('table', { name: /Failed Eventarc records, newest first/ })
    expect(within(table).getByText('Undelivered', { selector: 'span' })).toBeInTheDocument()
    expect(sourcesApi.fetchComponentSource).toHaveBeenCalledWith('ns-2', 'eventarc')
  })

  it('the Access tab says whether the connection can read the records component', async () => {
    open('/next/azure/c/event-grid/ns-3?tab=access')
    expect(await screen.findByRole('heading', { name: 'Access' })).toBeInTheDocument()
    expect(screen.getByText('Can read Event Grid')).toBeInTheDocument()
    expect(screen.getByText('Yes')).toBeInTheDocument()
  })

  it('has no accessibility violations on a records page or its not-enabled state', async () => {
    const { container, unmount } = open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('table')
    await expectNoAxeViolations(container)
    unmount()

    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue({ ...readable('event-grid', 'x'), available: false, places: [], reason: 'Not enabled for this connection: needs Reader.' })
    const second = open('/next/azure/c/event-grid/ns-3')
    await screen.findByRole('heading', { name: 'Not enabled' })
    await expectNoAxeViolations(second.container)
  })
})
