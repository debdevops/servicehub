import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, entity, me } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn() }))

const generatedAtUtc = '2026-10-18T10:00:00Z'
const ns = (id: string, over: Partial<namespacesApi.Namespace> = {}) => ({ id, name: id, awsRegion: null, gcpProjectId: null, ...over }) as namespacesApi.Namespace

function open(url: string, components: ReturnType<typeof component>[], namespaces: namespacesApi.Namespace[] = []) {
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components, generatedAtUtc })
  vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue(namespaces)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('the component list (unit D1)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
  })

  it('lists the connections of this component on this cloud, with a breadcrumb back to Home', async () => {
    open('/next/azure/c/service-bus', [component({ namespaceId: 'a', name: 'orders-sb' }), component({ namespaceId: 'b', name: 'billing-sb', needsYou: 0, approvals: 0, canSend: false, deadLetters: { value: 0, kind: 'exact' } })])
    expect(await screen.findByRole('heading', { name: 'Service Bus' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toHaveTextContent('Azure')
    expect(within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', { name: 'Azure' })).toHaveAttribute('href', '/next/azure')
    expect(screen.getByRole('heading', { name: 'Service Bus connections (2)' })).toBeInTheDocument()

    const table = screen.getByRole('table', { name: /Service Bus connections on Azure/ })
    const first = within(table).getByRole('row', { name: /orders-sb/ })
    expect(within(first).getByText('Needs you')).toBeInTheDocument()
    expect(within(first).getByText('Read + Send')).toBeInTheDocument()
    const second = within(table).getByRole('row', { name: /billing-sb/ })
    expect(within(second).getByText('No items need attention')).toBeInTheDocument()
    expect(within(second).getByText('Read only')).toBeInTheDocument()
  })

  it('says a read-only connection offers no Replay, and only when one is read-only', async () => {
    const { unmount } = open('/next/azure/c/service-bus', [component({ canSend: false })])
    expect(await screen.findByText(/A read-only connection shows everything/)).toBeInTheDocument()
    unmount()
    open('/next/azure/c/service-bus', [component()])
    await screen.findByRole('table')
    expect(screen.queryByText(/A read-only connection shows everything/)).not.toBeInTheDocument()
  })

  it('is filtered by component: one AWS connection is two components, and each has its own list', async () => {
    const sqs = component({ namespaceId: 'x', cloud: 'aws', componentKey: 'sqs', componentName: 'SQS', name: 'prod-aps1' })
    const sns = component({ namespaceId: 'x', cloud: 'aws', componentKey: 'sns', componentName: 'SNS', name: 'prod-aps1', entities: [entity(), entity({ name: 'b' })] })
    coverageRows.push({ ...coverageRows[2], id: 'sns.subscriber-dead-letter', componentKey: 'sns', name: 'SNS' })
    try {
      open('/next/aws/c/sns', [sqs, sns])
      const table = await screen.findByRole('table', { name: /SNS connections on AWS/ })
      expect(within(table).getAllByRole('row')).toHaveLength(2) // the header and the one SNS row
      expect(within(table).getByRole('row', { name: /prod-aps1/ })).toHaveTextContent('2') // its two places
    } finally {
      coverageRows.pop()
    }
  })

  it('shows region or project only when a connection has one, taken from what is stored', async () => {
    const { unmount } = open('/next/aws/c/sqs', [component({ namespaceId: 'x', cloud: 'aws', componentKey: 'sqs', name: 'prod' })], [ns('x', { awsRegion: 'eu-west-1' })])
    expect(await screen.findByRole('columnheader', { name: 'Region / project' })).toBeInTheDocument()
    expect(screen.getByText('eu-west-1')).toBeInTheDocument()
    unmount()
    open('/next/azure/c/service-bus', [component()], [ns('ns-azure')])
    await screen.findByRole('table')
    expect(screen.queryByRole('columnheader', { name: 'Region / project' })).not.toBeInTheDocument()
  })

  it('View opens that connection’s own page, and Connect opens the connect dialog for this component', async () => {
    open('/next/azure/c/service-bus', [component({ namespaceId: 'ns-9', name: 'orders-sb' })])
    expect(await screen.findByRole('link', { name: 'View orders-sb' })).toHaveAttribute('href', '/next/azure/c/service-bus/ns-9')
    expect(screen.getByRole('link', { name: /Connect a Service Bus connection/ })).toHaveAttribute('href', '/next/azure/c/service-bus?dialog=connect&component=service-bus')
  })

  it('an unreachable connection says so, with the reason, and shows no counts', async () => {
    open('/next/azure/c/service-bus', [component({ reachable: false, lastError: 'denied', needsYou: 0, approvals: 0, deadLetters: { value: null, kind: 'notRead' }, observedAtUtc: null, entities: [] })])
    const row = await screen.findByRole('row', { name: /orders-sb/ })
    expect(within(row).getAllByText('Can’t reach')).toHaveLength(2)
    expect(within(row).getByText('denied')).toBeInTheDocument()
    expect(within(row).getByText('Not read')).toBeInTheDocument()
  })

  it('no connections: says so and offers Connect, rather than an empty table', async () => {
    open('/next/azure/c/service-bus', [])
    expect(await screen.findByText(/No Service Bus connection is connected yet/)).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows nothing rather than wrong numbers when the read fails', async () => {
    vi.mocked(componentsApi.fetchComponents).mockRejectedValue(new Error('down'))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: ['/next/azure/c/service-bus'] })} /></QueryClientProvider>)
    expect(await screen.findByText(/connections could not be read/)).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('pages 25 at a time', async () => {
    const many = Array.from({ length: 30 }, (_, i) => component({ namespaceId: `n${i}`, name: `conn-${i}` }))
    open('/next/azure/c/service-bus', many)
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Pages of Service Bus connections' })).toBeInTheDocument()
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(26)
  })

  it.each([
    ['a component the catalogue does not read as a store', '/next/azure/c/event-grid'],
    ['a component that does not exist', '/next/azure/c/nope'],
    ['a component on the wrong cloud', '/next/aws/c/service-bus'],
    ['an unknown cloud', '/next/oracle/c/service-bus'],
  ])('%s is not a page', async (_name, url) => {
    open(url, [])
    expect(await screen.findByRole('heading', { name: 'That page doesn’t exist' })).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/c/service-bus', [component(), component({ namespaceId: 'b', name: 'billing-sb', canSend: false })])
    await screen.findByRole('table')
    await expectNoAxeViolations(container)
  })
})
