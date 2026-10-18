import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import { AxiosError, type AxiosResponse } from 'axios'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as deadLettersApi from '@/lib/api/deadLetters'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import * as signaturesApi from '@/lib/api/signatures'
import { entityLabel, entityRows, fullName } from '@/next/lib/entityRows'
import { credentialWords, countsWords } from '@/next/lib/words'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, entity, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/signatures', async (o) => ({ ...(await o<typeof signaturesApi>()), fetchSignatures: vi.fn(), fetchSignatureTrust: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn(), lookAtDeadLetters: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetters: vi.fn() }))

const generatedAtUtc = '2026-10-18T10:00:00Z'
const caps = (over: Partial<namespacesApi.ProviderCapabilities> = {}): namespacesApi.ProviderCapabilities => ({
  supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, supportsRecoveryMarker: true,
  canProveDlqAbsence: true, supportsTopics: true, supportsSubscriptions: true, countsAreApproximate: false, notes: '', ...over,
})
const namespace = (over: Partial<namespacesApi.Namespace> = {}) => ({
  id: 'ns-azure', name: 'orders-sb', displayName: null, description: null, provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null,
  isActive: true, createdAt: generatedAtUtc, lastConnectionTestAt: null, lastConnectionTestSucceeded: true, capabilities: caps(), ...over,
}) as namespacesApi.Namespace
const listed = (entities: Partial<namespacesApi.Entity>[]) => ({ namespaceId: 'x', entities: entities.map((e) => ({ name: 'q', kind: 'queue', activeMessages: null, deadLetterMessages: null, deadLetterTargetName: null, ...e })) }) as namespacesApi.EntityList
const letter = (id: number, over: Partial<deadLettersApi.DeadLetter> = {}) => ({
  id, namespaceId: 'ns-azure', messageId: `m-${id}`, sequenceNumber: id, entityName: 'orders-queue', entityType: 'queue', topicName: null, detectedAtUtc: generatedAtUtc, enqueuedTimeUtc: generatedAtUtc,
  deliveryCount: 5, sizeInBytes: 10, deadLetterReason: 'ProcessingError', deadLetterErrorDescription: null, status: 'active', ...over,
}) as deadLettersApi.DeadLetter

const azure = () => component({
  entities: [
    entity({ name: 'orders-queue', needsYou: 0, deadLetters: { value: 0, kind: 'exact' } }),
    entity({ name: 'sub-1', parent: 'payments-topic', kind: 'subscription', needsYou: 1, deadLetters: { value: 2, kind: 'exact' } }),
    entity({ name: 'sub-2', parent: 'payments-topic', kind: 'subscription', needsYou: 0, deadLetters: { value: 0, kind: 'exact' } }),
  ],
})
const aws = () => component({
  namespaceId: 'ns-aws', componentKey: 'sqs', cloud: 'aws', name: 'prod-aps1', componentName: 'SQS', watched: false, class: 'r2ReplayableUnproven', deadLetters: { value: 7840, kind: 'approximate' },
  recorded: { messages: 200, reachedLimit: false, atUtc: null }, needsYou: 3, approvals: 2, handOffs: 1,
  entities: [
    entity({ name: 'orders-queue', deadLetterStore: 'orders-queue-dlq', deadLetters: { value: 6420, kind: 'approximate' }, recorded: { messages: 100, reachedLimit: true, atUtc: null }, needsYou: 3 }),
    entity({ name: 'audit-queue', deadLetterStore: null, deadLetters: { value: 0, kind: 'approximate' }, recorded: { messages: null, reachedLimit: false, atUtc: null } }),
  ],
})
const gcp = () => component({
  namespaceId: 'ns-gcp', componentKey: 'pubsub', cloud: 'gcp', name: 'servicehub-prod', componentName: 'Pub/Sub', watched: false, class: 'r2ReplayableUnproven', deadLetters: { value: null, kind: 'notReported' },
  recorded: { messages: 137, reachedLimit: false, atUtc: null }, needsYou: 2, approvals: 1, handOffs: 1,
  entities: [
    entity({ name: 'orders-sub', parent: 'orders-topic', kind: 'subscription', deadLetterStore: 'orders-dead', deadLetters: { value: null, kind: 'notReported' }, recorded: { messages: 100, reachedLimit: true, atUtc: null } }),
    entity({ name: 'events-sub', parent: 'events-topic', kind: 'subscription', deadLetterStore: 'events-dead', deadLetters: { value: null, kind: 'notReported' }, recorded: { messages: null, reachedLimit: false, atUtc: null } }),
    entity({ name: 'audit-sub', parent: 'orders-topic', kind: 'subscription', deadLetterStore: null, deadLetters: { value: null, kind: 'notReported' }, recorded: { messages: 37, reachedLimit: false, atUtc: null } }),
  ],
})

function open(url: string, c = azure(), ns = namespace(), active: Partial<namespacesApi.Entity>[] = [], arrange?: () => void) {
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue(ns)
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue(listed(active))
  arrange?.() // after the defaults, so a test can make one read fail
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}
const ok = '/next/azure/c/service-bus/ns-azure'

describe('entityRows', () => {
  const e = (name: string, parent: string | null = null) => entity({ name, parent, kind: parent ? 'subscription' : 'queue' })
  it('puts each topic before its subscriptions and queues after, whatever order the cloud listed them in', () => {
    const rows = entityRows([e('zeta'), e('b', 'beta'), e('a', 'alpha'), e('c', 'beta'), e('alpha-queue')], () => null)
    expect(rows.map((r) => (r.kind === 'topic' ? `topic ${r.name}` : r.entity.name))).toEqual(['topic alpha', 'a', 'topic beta', 'b', 'c', 'alpha-queue', 'zeta'])
  })
  it('names a subscription by the path the entities endpoint uses', () => {
    expect(fullName({ name: 'sub-1', parent: 'payments-topic' })).toBe('payments-topic/subscriptions/sub-1')
    expect(fullName({ name: 'orders-queue', parent: null })).toBe('orders-queue')
  })
})

describe('entityLabel', () => {
  it('shows a stored subscription path as topic / name, never the topic twice', () => {
    expect(entityLabel('orders-topic/subscriptions/orders-subscription', 'orders-topic')).toBe('orders-topic / orders-subscription')
    expect(entityLabel('orders-topic/subscriptions/orders-subscription', null)).toBe('orders-topic / orders-subscription')
  })
  it('joins a bare name with its topic, and leaves a queue alone', () => {
    expect(entityLabel('sub-1', 'payments-topic')).toBe('payments-topic / sub-1')
    expect(entityLabel('orders-queue', null)).toBe('orders-queue')
  })
})

describe('the credential and count words', () => {
  it('names how a connection signs in without ever showing the credential, and an unknown type as stored', () => {
    expect(credentialWords('connectionString')).toBe('Connection string')
    expect(credentialWords('awsOidc')).toBe('OIDC federation')
    expect(credentialWords('somethingNew')).toBe('somethingNew')
  })
  it('says what the counts are', () => {
    expect(countsWords('AWS', { value: 1, kind: 'approximate' })).toBe('Approximate, as AWS reports them')
    expect(countsWords('Google Cloud', { value: null, kind: 'notReported' })).toBe('Not reported by Google Cloud')
  })
})

describe('one connection’s page (unit D2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(signaturesApi.fetchSignatures).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 4, all: 0, growing: 0, replayHelps: 0, replayDoesNotHelp: 0 })
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue({ items: [], paging: { total: 0, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] })
  })

  it('shows the connection, its status, its tiles and a breadcrumb back through the list to Home', async () => {
    open(ok, azure(), namespace(), [{ name: 'orders-queue', activeMessages: 412 }, { name: 'payments-topic/subscriptions/sub-1', activeMessages: 96 }, { name: 'payments-topic/subscriptions/sub-2', activeMessages: 0 }])
    expect(await screen.findByRole('heading', { level: 1, name: /orders-sb/ })).toHaveTextContent('Needs you (1)')
    const crumbs = screen.getByRole('navigation', { name: 'Breadcrumb' })
    expect(within(crumbs).getByRole('link', { name: 'Azure' })).toHaveAttribute('href', '/next/azure')
    expect(within(crumbs).getByRole('link', { name: 'Service Bus' })).toHaveAttribute('href', '/next/azure/c/service-bus')

    expect(await screen.findByTestId('tile-first')).toHaveTextContent('12')
    expect(screen.getByTestId('tile-needs')).toHaveTextContent('1')
    expect(await screen.findByTestId('tile-active')).toHaveTextContent('508') // 412 + 96 + 0
    expect(screen.queryByTestId('tile-recorded')).not.toBeInTheDocument()
    expect(screen.queryByTestId('tile-store')).not.toBeInTheDocument() // Azure has no separate dead-letter place
    expect(screen.queryByRole('region', { name: 'Not looked yet' })).not.toBeInTheDocument()
  })

  it('lays places out as the cloud does: a topic with its subscriptions beneath, and says a topic holds nothing itself', async () => {
    open(ok, azure(), namespace(), [{ name: 'orders-queue', activeMessages: 412 }])
    const table = await screen.findByRole('table', { name: /Queues, topics and subscriptions of orders-sb/ })
    const names = within(table).getAllByRole('rowheader').map((r) => r.textContent?.trim())
    expect(names).toEqual(['payments-topic', 'sub-1', 'sub-2', 'orders-queue'])
    expect(within(table).getByText('A topic holds nothing itself. Its subscriptions do.')).toBeInTheDocument()
    expect(within(table).queryByRole('columnheader', { name: 'Dead-letter place' })).not.toBeInTheDocument()
    expect(within(table).queryByRole('columnheader', { name: 'Recorded' })).not.toBeInTheDocument()
    expect(await within(table).findByRole('columnheader', { name: 'Active' })).toBeInTheDocument()
  })

  it('an unwatched, approximate cloud: Recorded tile and column, the not-watched notice, a dead-letter place column, "None set"', async () => {
    open('/next/aws/c/sqs/ns-aws', aws(), namespace({ id: 'ns-aws', provider: 'aws', awsRegion: 'ap-south-1', authType: 'awsAccessKey', capabilities: caps({ supportsRepeatablePeek: false, canProveDlqAbsence: false, countsAreApproximate: true }) }),
      [{ name: 'orders-queue', activeMessages: 1870 }, { name: 'audit-queue', activeMessages: 450 }])
    expect(await screen.findByTestId('tile-first')).toHaveTextContent('7,840(approx)')
    expect(screen.getByTestId('tile-recorded')).toHaveTextContent('200')
    expect(await screen.findByTestId('tile-active')).toHaveTextContent('2,320(approx)')
    expect(screen.getByTestId('tile-store')).toHaveTextContent('1 of 2')
    expect(screen.getByRole('note')).toHaveTextContent('AWS is not watched in the background.')

    const table = screen.getByRole('table', { name: /Queues, topics and subscriptions/ })
    expect(within(table).getByRole('columnheader', { name: 'Dead-letter place' })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: 'Recorded' })).toBeInTheDocument()
    expect(within(table).getByText('orders-queue-dlq')).toBeInTheDocument()
    expect(within(table).getByText('None set')).toBeInTheDocument()
    expect(within(table).getByText('100 (limit)')).toBeInTheDocument()
    expect(within(table).getByText('Not looked yet')).toBeInTheDocument()
  })

  it('a cloud that reports no counts: "Dead letters recorded", no Active column or tile, "Not looked yet" counted', async () => {
    open('/next/gcp/c/pubsub/ns-gcp', gcp(), namespace({ id: 'ns-gcp', provider: 'gcp', gcpProjectId: 'servicehub-prod', authType: 'gcpWorkloadIdentity', capabilities: caps({ supportsMessageCounts: false, supportsRepeatablePeek: false }) }))
    expect(await screen.findByRole('region', { name: 'Dead letters recorded' })).toBeInTheDocument()
    expect(screen.getByTestId('tile-first')).toHaveTextContent('137')
    expect(screen.queryByTestId('tile-active')).not.toBeInTheDocument()
    expect(screen.getByTestId('tile-store')).toHaveTextContent('2 of 3')
    expect(screen.getByTestId('tile-not-looked')).toHaveTextContent('1')
    const table = screen.getByRole('table', { name: /Queues, topics and subscriptions/ })
    expect(within(table).queryByRole('columnheader', { name: 'Active' })).not.toBeInTheDocument()
    expect(within(table).getAllByText('Can’t count here').length).toBeGreaterThan(0)
  })

  it('Component information names how it signs in, never the credential, and what the counts are', async () => {
    open(ok, azure(), namespace({ description: 'secret-looking-text' }))
    const info = await screen.findByRole('region', { name: 'Component information' })
    expect(info).toHaveTextContent('Signs in withConnection string')
    expect(info).toHaveTextContent('WatchedAutomatically')
    expect(info).toHaveTextContent('CountsExact')
    expect(info).toHaveTextContent('Connected · Read + Send')
    expect(info).not.toHaveTextContent('secret-looking-text')
  })

  it('an unreachable connection says why, in the header, the table and the information', async () => {
    open(ok, component({ reachable: false, lastError: 'denied', needsYou: 0, approvals: 0, deadLetters: { value: null, kind: 'notRead' }, observedAtUtc: null, entities: [] }))
    expect(await screen.findByTestId('tile-first')).toHaveTextContent('Can’t read')
    expect(screen.getByText('Its places cannot be listed until it can be reached.')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Component information' })).toHaveTextContent('Can’t reach — denied')
  })

  it('the dead-letter preview says where each message stands, from the same pending-work read, and links the tab', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue({ items: [letter(1), letter(2), letter(3)], paging: { total: 9, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 1, entryId: 'e1' }), handOff({ dlqMessageId: 2, entryId: 'e2' })]))
    open(ok)
    const preview = await screen.findByRole('region', { name: /Dead-letter messages \(9\)/ })
    expect(await within(preview).findByText('Waiting for approval')).toBeInTheDocument()
    expect(within(preview).getByText('Handed to a person')).toBeInTheDocument()
    expect(within(preview).getByText('In dead-letter')).toBeInTheDocument()
    expect(within(preview).getByRole('link', { name: 'Review' })).toHaveAttribute('href', `${ok}?dialog=review&entry=e1`)
    expect(within(preview).getAllByRole('link', { name: 'View' })[0]).toHaveAttribute('href', `${ok}?panel=hand-off&entry=e2`)
    expect(within(preview).getByRole('link', { name: /Open the Dead-letter messages tab/ })).toHaveAttribute('href', `${ok}?tab=dead-letters`)
  })

  it('a Viewer sees "Needs an Approver" in place of Review', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer', grantors: ['maya'] }))
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue({ items: [letter(1)], paging: { total: 1, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 1 })]))
    open(ok)
    const preview = await screen.findByRole('region', { name: /Dead-letter messages \(1\)/ })
    expect(await within(preview).findByText('Needs an Approver')).toBeInTheDocument()
    expect(within(preview).queryByRole('link', { name: 'Review' })).not.toBeInTheDocument()
  })

  it('an unwatched connection calls its messages recorded, never "in dead-letter"', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue({ items: [letter(1)], paging: { total: 200, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] })
    open('/next/aws/c/sqs/ns-aws', aws(), namespace({ id: 'ns-aws', provider: 'aws', capabilities: caps({ supportsRepeatablePeek: false }) }))
    const preview = await screen.findByRole('region', { name: /Recorded dead-letter messages \(showing 1 of 200\)/ })
    expect(within(preview).getByText('Recorded', { selector: 'td span' })).toBeInTheDocument() // the state chip (the column is also headed Recorded)
    expect(within(preview).queryByText('In dead-letter')).not.toBeInTheDocument()
  })

  it('the tabs are links in the URL: a named tab opens, an unknown or missing one is the Overview, and Stuck appears only where the catalogue has it', async () => {
    // `stuck` is the one tab still unbuilt (unit G5); the address names it even though its link appears only where the catalogue has it.
    const { unmount } = open(`${ok}?tab=stuck`)
    expect(await screen.findByRole('heading', { level: 1, name: 'Component detail · Stuck messages' })).toBeInTheDocument()
    unmount()

    const { unmount: again } = open(`${ok}?tab=nonsense`)
    expect(await screen.findByTestId('tile-first')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Overview' })).toHaveAttribute('aria-current', 'page')
    again()

    coverageRows.push({ ...coverageRows[0], id: 'service-bus.stuck', detection: 'signal', status: 'beingValidated', inSidebar: false })
    try {
      open(ok)
      expect(await screen.findByRole('link', { name: 'Stuck messages' })).toHaveAttribute('href', `${ok}?tab=stuck`)
    } finally {
      coverageRows.pop()
    }
  })

  it('pages the places 25 at a time', async () => {
    const many = component({ entities: Array.from({ length: 30 }, (_, i) => entity({ name: `queue-${String(i).padStart(2, '0')}` })) })
    open(ok, many)
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Pages of queues, topics and subscriptions' })).toBeInTheDocument()
  })

  it.each([
    ['a component on the wrong cloud', '/next/aws/c/service-bus/ns-azure'],
    ['a component the catalogue does not list', '/next/azure/c/event-grid/ns-azure'],
  ])('%s is not a page', async (_name, url) => {
    open(url)
    expect(await screen.findByRole('heading', { name: 'That page doesn’t exist' })).toBeInTheDocument()
  })

  it('a connection the API says does not exist (404) is not a page', async () => {
    const notFound = new AxiosError('nf', 'ERR_BAD_REQUEST', undefined, undefined, { status: 404, data: {} } as AxiosResponse)
    open(ok, azure(), namespace(), [], () => vi.mocked(componentsApi.fetchComponent).mockRejectedValue(notFound))
    expect(await screen.findByRole('heading', { name: 'That page doesn’t exist' })).toBeInTheDocument()
  })

  it('shows nothing rather than wrong numbers when the connection cannot be read (any failure but a 404)', async () => {
    open(ok, azure(), namespace(), [], () => vi.mocked(componentsApi.fetchComponent).mockRejectedValue(new Error('down')))
    expect(await screen.findByText(/This connection could not be read/)).toBeInTheDocument()
    expect(screen.queryByTestId('tile-first')).not.toBeInTheDocument()
  })

  it('has no accessibility violations on an unwatched connection with everything showing', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue({ items: [letter(1), letter(2)], paging: { total: 2, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 1 }), handOff({ dlqMessageId: 2 })]))
    const { container } = open('/next/gcp/c/pubsub/ns-gcp', gcp(), namespace({ id: 'ns-gcp', provider: 'gcp', capabilities: caps({ supportsMessageCounts: false, supportsRepeatablePeek: false }) }))
    await screen.findByRole('region', { name: /Recorded dead-letter messages/ })
    await screen.findByText('Waiting for approval')
    await expectNoAxeViolations(container)
  })
})
