import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as messagesApi from '@/lib/api/messages'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, entity, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/messages', async (o) => ({ ...(await o<typeof messagesApi>()), peekMessages: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), fetchEntities: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const caps = (over: Partial<namespacesApi.ProviderCapabilities> = {}): namespacesApi.ProviderCapabilities => ({
  supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, supportsRecoveryMarker: true,
  canProveDlqAbsence: true, supportsTopics: true, supportsSubscriptions: true, countsAreApproximate: false, notes: '', ...over,
})
const ns = (over: Partial<namespacesApi.Namespace> = {}) => ({
  id: 'ns-azure', name: 'orders-sb', displayName: null, description: null, provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null,
  isActive: true, createdAt: at, lastConnectionTestAt: null, lastConnectionTestSucceeded: true, capabilities: caps(), ...over,
}) as namespacesApi.Namespace
const live = (...e: Partial<namespacesApi.Entity>[]) => ({ namespaceId: 'x', entities: (e.length ? e : [{}]).map((x) => ({ name: 'orders-queue', kind: 'queue', activeMessages: 412, deadLetterMessages: 0, deadLetterTargetName: null, ...x })) }) as namespacesApi.EntityList

const sqs = () => component({
  namespaceId: 'ns-aws', componentKey: 'sqs', cloud: 'aws', name: 'prod-aps1', componentName: 'SQS', watched: false, class: 'r2ReplayableUnproven',
  deadLetters: { value: 8, kind: 'approximate' }, entities: [entity({ name: 'files-queue', deadLetters: { value: 8, kind: 'approximate' } })],
})

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Active messages (unit E3)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component({ entities: [entity({ deadLetters: { value: 2, kind: 'exact' } }), entity({ name: 'orders-topic', kind: 'topic' })] })], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns()])
    vi.mocked(namespacesApi.fetchEntities).mockResolvedValue(live())
    vi.mocked(messagesApi.peekMessages).mockResolvedValue({ messages: [], paging: { requested: 100, returned: 0, nextFromSequenceNumber: null } } as never)
  })

  it('asks for its own cloud, and for every cloud when the scope is all', async () => {
    open('/next/azure/active')
    await screen.findByRole('heading', { level: 1, name: 'Active messages' })
    await waitFor(() => expect(componentsApi.fetchComponents).toHaveBeenCalledWith('azure'))
    open('/next/all/active')
    await waitFor(() => expect(componentsApi.fetchComponents).toHaveBeenCalledWith(undefined))
  })

  it('lists the places that hold messages with their exact count, and leaves a topic out', async () => {
    open('/next/azure/active')
    const row = (await screen.findByText('orders-queue')).closest('tr')!
    // The counts come from a second read, so the row first says "Not read" rather than a number it does not have.
    await waitFor(() => expect(within(row).getByText('412')).toBeInTheDocument())
    expect(within(row).getByText('2')).toBeInTheDocument()
    expect(screen.queryByText('orders-topic')).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('Active on Azure').parentElement).toHaveTextContent('412'))
  })

  it('offers Browse only where looking changes nothing, and never peeks where it would', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [sqs()], generatedAtUtc: at })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns({ id: 'ns-aws', provider: 'aws', capabilities: caps({ supportsRepeatablePeek: false, countsAreApproximate: true }) })])
    vi.mocked(namespacesApi.fetchEntities).mockResolvedValue(live({ name: 'files-queue', activeMessages: 310 }))
    open('/next/aws/active')
    const row = (await screen.findByText('files-queue')).closest('tr')!
    expect(within(row).getByText('310 (approx)')).toBeInTheDocument()
    expect(within(row).getByText('Browsing not offered')).toBeInTheDocument()
    expect(within(row).queryByRole('link', { name: 'Browse' })).not.toBeInTheDocument()
    expect(screen.getByText(/count as a delivery attempt/)).toBeInTheDocument()
    expect(messagesApi.peekMessages).not.toHaveBeenCalled()
  })

  it('says "Can’t count here", never 0, for a cloud that reports no counts', async () => {
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns({ capabilities: caps({ supportsMessageCounts: false, supportsRepeatablePeek: false }) })])
    open('/next/azure/active')
    const row = (await screen.findByText('orders-queue')).closest('tr')!
    expect(within(row).getByText('Can’t count here')).toBeInTheDocument()
    expect(screen.getByText('Active on Azure').parentElement).toHaveTextContent('Can’t count here')
  })

  it('opens the browser on the chosen place from the address, and the peek is for that place only', async () => {
    const { router } = open('/next/azure/active?queue=ns-azure%7Corders-queue')
    await screen.findByRole('heading', { name: 'Browsing active messages' })
    await waitFor(() => expect(messagesApi.peekMessages).toHaveBeenCalledWith('ns-azure', expect.objectContaining({ entity: 'orders-queue' })))
    expect(router.state.location.search).toContain('queue=')
  })

  it('shows an error, not a table, when the read fails; and says so when nothing is connected', async () => {
    vi.mocked(componentsApi.fetchComponents).mockRejectedValueOnce(new Error('boom'))
    open('/next/azure/active')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be read')
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: at })
    open('/next/gcp/active')
    expect(await screen.findByText(/No queue or subscription is connected/)).toBeInTheDocument()
  })

  it('adds a Cloud column for all clouds, and no sum across them', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component(), sqs()], generatedAtUtc: at })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns(), ns({ id: 'ns-aws', provider: 'aws' })])
    open('/next/all/active')
    expect((await screen.findAllByRole('columnheader', { name: 'Cloud' })).length).toBe(2) // the table, and the capabilities table
    expect(screen.getByText('Active on Azure')).toBeInTheDocument()
    expect(screen.getByText('Active on AWS')).toBeInTheDocument()
    expect(screen.queryByText(/^Across \d+ components?$/)).not.toBeInTheDocument() // the dead-letter tile is for one cloud only
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/active')
    await screen.findByText('orders-queue')
    await expectNoAxeViolations(container)
  })
})
